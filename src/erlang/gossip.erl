-module(gossip).

-define(PROBE_FANOUT, 3).
-define(INDIRECT_PING_FANOUT, 3).
-define(PING_TIMEOUT_MS, 30).
-define(INDIRECT_PING_TIMEOUT_MS, 60).
-define(PRUNE_TIMEOUT_MS, 60000).

-export([ start/2
        , start/3
        , stop/1
        , report/1
        , listener/3
        , local_ip_v4/0
        , connect/3
        ]).

-import(gen_udp, [open/2, send/4, close/1]).

-include_lib("eunit/include/eunit.hrl").

-type ip_address() ::
        inet:ip4_address().

-type ip_port() ::
        0..65535.

-type address() ::
        { ip_address(), ip_port() }.

-type service_type() ::
        byte().

-type event() ::
        { alive,      address(), generation(), service_type(), ip_port() } |
        { suspicious, address(), generation(), service_type(), ip_port() } |
        { dead,       address(), generation(), service_type(), ip_port() } |
        { left,       address(), generation(), service_type(), ip_port() }.

-type node_states() ::
        maps:map(address(), {num_broadcasts(), node_state()}).

-type awaiting_replies() ::
        maps:map(address(), timer:timer()).

-type node_health() ::
        alive | supicious | dead | left.

-type generation() ::
        byte().

-type num_broadcasts() ::
        integer().

-type node_state() ::
        { node_health(), generation(), service_type(), ip_port() }.

-record(state, { socket       :: port()
               , nodes        :: node_states()
               , waiting      :: awaiting_replies()
               , generation   :: byte()
               , service      :: byte()
               , service_port :: ip_port()
               }).

-define(LATER_GENERATION(Gen1, Gen2),
        (((0 < (Gen1 - Gen2)) and ((Gen1 - Gen2) < 191)) or ((Gen1 - Gen2) =< -191))).


later_generation_test_() ->
    [?_assert(?LATER_GENERATION((A + B) rem 256, A))
     || A <- lists:seq(0, 255), B <- lists:seq(1, 64)].


-spec max_generation(byte(), byte()) -> byte().

max_generation(G1, G2)
  when ?LATER_GENERATION(G1, G2) ->
    G1;

max_generation(_, G) ->
    G.


-spec merge_node_state(node_state(), node_state()) -> node_state().

merge_node_state({alive, G, _, _}, R={_, G, _, _}) ->
    R;
merge_node_state(L={_, G, _, _}, {alive, G, _, _}) ->
    L;
merge_node_state(L={left, G, _, _}, {_, G, _, _}) ->
    L;
merge_node_state({_, G, _, _}, R={left, G, _, _}) ->
    R;
merge_node_state(L={dead, G, _, _}, {suspicious, G, _, _}) ->
    L;
merge_node_state(L={_, G1, _, _}, {_, G2, _, _})
  when ?LATER_GENERATION(G1, G2)->
    L;
merge_node_state(_, R) ->
    R.


-spec merge_node({num_broadcasts(), node_state()}, {num_broadcasts(), node_state()}) -> {num_broadcasts(), node_state()}.

merge_node({Lnum, Lstate}, {Rnum, Rstate}) ->
    case merge_node_state(Lstate, Rstate) of
        Lstate ->
            {Lnum, Lstate};
        Rstate ->
            {Rnum, Rstate}
    end.


-spec process_event(event(), #state{}) -> #state{}.

process_event({Type, Address, Generation, Service, ServicePort}, State=#state{nodes=Nodes}) ->
    Node={0, {Type, Generation, Service, ServicePort}},
    Relevance = case Type of
        alive -> always;
        suspicious -> always;
        dead -> present;
        left -> present
    end,
    ShouldStore = case {Relevance, maps:find(Address, Nodes)} of
                      {always, _} -> yes;
                      {present, {ok, _}} -> yes;
                      _ -> no
                  end,
    case ShouldStore of
        yes -> State#state{nodes=maps:update_with(Address, fun(N) -> merge_node(N, Node) end, Node, Nodes)};
        no -> State
    end.


process_events(Events, State) ->
    lists:foldl(fun process_event/2, State, Events).


-spec shuffle(list()) -> list().

shuffle(L) ->
    RandomlyValued = [ {rand:uniform(), N} || N <- L ],
    [ X || {_,X} <- lists:sort(RandomlyValued)].


-spec parse_event(<<_:64, _:_*8>>) -> {event(), binary()}.

parse_event(<<A:8, B:8, C:8, D:8, Port:16, 0:8, Generation:8,
              Service:8, ServicePort:16,
              Rest/binary>>) ->
    {{alive, {{A, B, C, D}, Port}, Generation, Service, ServicePort}, Rest};

parse_event(<<A:8, B:8, C:8, D:8, Port:16, 1:8, Generation:8,
              Rest/binary>>) ->
    {{suspicious, {{A, B, C, D}, Port}, Generation, 0, 0}, Rest};

parse_event(<<A:8, B:8, C:8, D:8, Port:16, 2:8, Generation:8,
              Rest/binary>>) ->
    {{dead, {{A, B, C, D}, Port}, Generation, 0, 0}, Rest};

parse_event(<<A:8, B:8, C:8, D:8, Port:16, 3:8, Generation:8,
              Rest/binary>>) ->
    {{left, {{A, B, C, D}, Port}, Generation, 0, 0}, Rest}.


-spec parse_events(binary()) -> [event()].

parse_events(<<>>) ->
    [];

parse_events(Message) ->
    case parse_event(Message) of
        {Event, Rest} ->
            [Event | parse_events(Rest)]
    end.


-spec unparse_event(address(), node_state()) -> <<_:64, _:_*24>>.

unparse_event({{A, B, C, D}, Port}, {alive, Generation, Service, ServicePort}) ->
    <<A:8, B:8, C:8, D:8, Port:16, 0:8, Generation:8, Service:8, ServicePort:16>>;

unparse_event({{A, B, C, D}, Port}, {suspicious, Generation, _, _}) ->
    <<A:8, B:8, C:8, D:8, Port:16, 1:8, Generation:8>>;

unparse_event({{A, B, C, D}, Port}, {dead, Generation, _, _}) ->
    <<A:8, B:8, C:8, D:8, Port:16, 2:8, Generation:8>>;

unparse_event({{A, B, C, D}, Port}, {left, Generation, _, _}) ->
    <<A:8, B:8, C:8, D:8, Port:16, 3:8, Generation:8>>.


-spec unparse_events([{address(), node_state()}], binary(), [address()]) -> {binary(), [address()]}.

unparse_events([{Addr, NodeState} | Events], Binary, UpdatesSent) ->
    EventBinary = unparse_event(Addr, NodeState),
    case byte_size(EventBinary) + byte_size(Binary) =< 508 of
        true -> unparse_events(Events, <<Binary/binary, EventBinary/binary>>, [Addr | UpdatesSent]);
        false -> {Binary, UpdatesSent}
    end;

unparse_events(_, Binary, UpdatesSent) ->
    {Binary, UpdatesSent}.


unparse_events(Events, Binary) ->
    unparse_events(Events, Binary, []).


-spec sort_events(node_states()) -> [{address(), node_state()}].

sort_events(Nodes) ->
    NodesList = maps:fold(
                  fun(Addr, {TimesSent, State}, Acc) ->
                          [{Addr, State, TimesSent} | Acc]
                  end, [], Nodes),
    [ {Addr, State} || {Addr, State, _} <- lists:keysort(3, NodesList) ].


-spec increment_times_sent(node_states(), [address()]) -> node_states().

increment_times_sent(Nodes, Addresses) ->
    lists:foldr(fun (Addr, NodeStates) -> 
                        maps:update_with(Addr, fun({U, S}) -> 
                                                       {U + 1, S}
                                               end,
                                         NodeStates)
                end, Nodes, Addresses).

-spec send_message(address(), binary(), #state{}) -> #state{}.

send_message(Address, Header, State=#state{
                                       socket=Socket,
                                       nodes=Nodes,
                                       generation=MyGeneration,
                                       service=MyService,
                                       service_port=MyServicePort
                                      }) ->
    {_, {ReceiverHealth, ReceiverGeneration, _, _}} = maps:get(Address, Nodes, {0, {dead, 0, 0, 0}}),
    ReceiverHealthByte = case ReceiverHealth of
                             alive -> 0;
                             suspicious -> 1;
                             dead -> 2;
                             left -> 3
                         end,
    FullHeader = <<0, % protocol version
                   Header/binary,
                   MyGeneration, MyService, MyServicePort:16,
                   ReceiverHealthByte, ReceiverGeneration>>,
    {Ip, Port} = Address,
    {Message, UpdatedAddresses} = unparse_events(sort_events(maps:without([Address], Nodes)), FullHeader),
    case send(Socket, Ip, Port, Message) of
        ok -> State#state{nodes=increment_times_sent(Nodes, UpdatedAddresses)};
        _ -> State
    end.


-spec send_ack(address(), #state{}) -> #state{}.

send_ack(Address, State) ->
    send_message(Address, <<0>>, State).


set_timer(Address, Timeout, Message, State=#state{waiting=Waiting}) ->
    case maps:find(Address, Waiting) of
        {ok, _} ->
            State; % there's already a timer set, so don't set another
        _ ->
            case timer:send_after(Timeout, Message) of
                {ok, Timer} ->
                    State#state{waiting=maps:put(Address, Timer, Waiting)};
                {error, Reason} ->
                    io:format("Could not start timer: ~p~n", [Reason]),
                    State
            end
    end.


-spec send_ping(address(), #state{}) -> #state{}.

send_ping(Address, State=#state{nodes=Nodes}) ->
    send_message(Address, <<1>>,
                 case maps:find(Address, Nodes) of
                     {ok, {_, NodeState}} ->
                         set_timer(Address, ?PING_TIMEOUT_MS, {indirect_ping, Address, NodeState}, State);
                     _ ->
                         NodeState = {dead, 0, 0, 0},
                         set_timer(Address, ?PING_TIMEOUT_MS, {indirect_ping, Address, NodeState},
                                   State#state{
                                     nodes=maps:put(Address, {0, NodeState}, Nodes)
                                    })
                 end).


-spec send_indirect_ack(address(), address(), #state{}) -> #state{}.

send_indirect_ack(Relay, {{A, B, C, D}, Port}, State) ->
    send_message(Relay, <<4, A, B, C, D, Port:16>>, State).


-spec send_indirect_ping(address(), address(), node_state(), #state{}) -> #state{}.

send_indirect_ping(Relay, Address={{A, B, C, D}, Port}, {_, Generation, Service, ServicePort}, State=#state{nodes=Nodes}) ->
    NodeState = {dead, Generation, Service, ServicePort},
    Node = {0, NodeState},
    send_message(Relay, <<5, A, B, C, D, Port:16>>,
                 set_timer(Address, ?INDIRECT_PING_TIMEOUT_MS, {mark_as_dead, Address, NodeState},
                           State#state{
                             nodes=maps:update_with(Address, fun(N) -> merge_node(N, Node) end, Node, Nodes)
                            })).

-spec forward_ack(address(), address(), #state{}) -> #state{}.

forward_ack({{A, B, C, D}, Port}, To, State) ->
    send_message(To, <<6, A, B, C, D, Port:16>>, State).


-spec forward_ping(address(), address(), #state{}) -> #state{}.

forward_ping({{A, B, C, D}, Port}, To, State) ->
    send_message(To, <<7, A, B, C, D, Port:16>>, State).


-spec mark_as_dead(address(), node_state(), #state{}) -> #state{}.

mark_as_dead(Address, {_, Generation, Service, ServicePort}, State=#state{nodes=Nodes}) ->
    NodeState = {dead, Generation, Service, ServicePort},
    Node = {0, NodeState},
    set_timer(Address, ?PRUNE_TIMEOUT_MS, {prune, Address, NodeState},
              State#state{
                nodes=maps:update_with(Address, fun(N) -> merge_node(N, Node) end, Node, Nodes)
               }).

-spec prune(address(), node_state(), #state{}) -> #state{}.

prune(Address, Node, State=#state{nodes=Nodes}) ->
    case maps:find(Address, Nodes) of
        {ok, {_, Node}} ->
            % if the entry in the map matches what we expect: remove it
            State#state{nodes=maps:remove(Address, Nodes)};
        _ ->
            State
    end.


random_addresses(Nodes, N) ->
    lists:sublist(shuffle(maps:keys(Nodes)), N).


-spec cancel_timer(address(), #state{}) -> #state{}.

cancel_timer(Address, State=#state{waiting=Waiting}) ->
    case maps:find(Address, Waiting) of
        {ok, Value} ->
            timer:cancel(Value),
            State#state{waiting=maps:remove(Address, Waiting)};
        _ ->
            State
    end.


process_membership_data(
  Address,
  <<SenderGeneration, SenderService, SenderServicePort:16, MyState, MyGeneration, Events/binary>>,
  State=#state{nodes=Nodes, generation=Generation}) ->
    NodeState = {alive, SenderGeneration, SenderService, SenderServicePort},
    Node = {0, NodeState},
    process_events(
      parse_events(Events),
      State#state{
        nodes=maps:update_with(Address, fun(N) -> merge_node(N, Node) end, Node, Nodes),
        generation=case MyState of
                       0 -> Generation;
                       _ -> max_generation(MyGeneration + 1, Generation)
                   end
       }).


process_udp_packet(Address, <<0, 0, Membership/binary>>, State) ->
    cancel_timer(Address, process_membership_data(Address, Membership, State));

process_udp_packet(Address, <<0, 1, Membership/binary>>, State) ->
    send_ack(Address,
             cancel_timer(Address, process_membership_data(Address, Membership, State)));

process_udp_packet(Address, <<0, 4, A, B, C, D, Port:16, Membership/binary>>, State) ->
    forward_ack(Address, {{A, B, C, D}, Port},
                cancel_timer(Address, process_membership_data(Address, Membership, State)));

process_udp_packet(Address, <<0, 5, A, B, C, D, Port:16, Membership/binary>>, State) ->
    forward_ping(Address, {{A, B, C, D}, Port},
                 cancel_timer(Address, process_membership_data(Address, Membership, State)));

process_udp_packet(Address, <<0, 6, A, B, C, D, Port:16, Membership/binary>>, State) ->
    % This is a forwarded ack, which means we can just treat it as if
    % we got an ack from the listed address and cancel the timer.
    cancel_timer({{A, B, C, D}, Port},
                 cancel_timer(Address, process_membership_data(Address, Membership, State)));

process_udp_packet(Address, <<0, 7, A, B, C, D, Port:16, Membership/binary>>, State) ->
    send_indirect_ack(Address, {{A, B, C, D}, Port},
                      cancel_timer(Address, process_membership_data(Address, Membership, State))).



-spec listener(#state{}) -> #state{}.

listener(State=#state{socket=Socket, nodes=Nodes, waiting=AwaitingReplies}) ->
    receive
        {probe} ->
            %% io:format("~p tick~n", [self()]),
            listener(
              lists:foldl(
                fun(Address, S) -> send_ping(Address, S) end,
                State,
                random_addresses(Nodes, ?PROBE_FANOUT)));

        {ping, Address} ->
            %% io:format("~p ping~n", [self()]),
            listener(send_ping(Address, State));

        {indirect_ping, Address, Node} ->
            listener(
              lists:foldl(
                fun(Relay, S) -> send_indirect_ping(Relay, Address, Node, S) end,
                cancel_timer(Address, State),
                random_addresses(Nodes, ?INDIRECT_PING_FANOUT)));

        {mark_as_dead, Address, Node} -> 
            listener(mark_as_dead(Address, Node, cancel_timer(Address, State)));

        {prune, Address, Node} ->
            listener(prune(Address, Node, cancel_timer(Address, State)));

        {udp, Socket, FromIp, FromPort, Message} ->
            %% io:format("~p mess~n", [self()]),
            listener(process_udp_packet({FromIp, FromPort}, Message, State));

        {report, ReplyTo} ->
            ReplyTo ! {report, Nodes},
            %% io:format("~p Nodes: ~p~n", [Tick, Nodes]),
            listener(State);

        {pause, Time} ->
            timer:sleep(Time),
            listener(State);

        {close, ReplyTo} ->
            io:format("closing ~p~n    ~p~n", [Nodes, AwaitingReplies]),
            close(Socket),
            ReplyTo ! ok,
            State;

        X ->
            io:format("Unknown message: ~p~n", [X]),
            listener(State)
    end.


-spec local_ip_v4() -> ip_address().

local_ip_v4() ->
    {ok, Addrs} = inet:getifaddrs(),
    case [Addr || {_, Opts} <- Addrs, {addr, Addr} <- Opts,
                  size(Addr) == 4, Addr =/= {127,0,0,1}] of
        [ Addr | _ ] ->
            Addr;
        _ ->
            error("Unable to detect IP address")
    end.


-spec listener(ip_port(), byte(), ip_port()) -> any().

listener(Port, Service, ServicePort) ->
    {ok, Socket} = open(Port, [binary, {active,true}]),
    timer:send_interval(1000, {probe}),
    listener(#state{
                socket=Socket,
                nodes=#{},
                waiting=#{},
                generation=0,
                service=Service,
                service_port=ServicePort
               }).


start(Service, ServicePort) ->
    start(0, Service, ServicePort).

start(Port, Service, ServicePort) ->
    spawn(?MODULE, listener, [Port, Service, ServicePort]).


stop(Pid) ->
    Pid ! {close, self()}.


connect(Pid, Ip, Port) ->
   Pid ! {ping, {Ip, Port}}. 


report(Pid) ->
    Pid ! { report, self() },
    receive
        {report, Nodes} ->
            Nodes
    end.
