-module(gossip).

-export([start/0, listener/2, merge_node_state/2]).

-include_lib("eunit/include/eunit.hrl").

-import(lists, [foldl/3]).
-import(orddict, []).

-type ip_address() :: inet:ip4_address().
-type ip_port() :: 0..65535.

-type address() :: {ip_address(), ip_port()}.

-type event() :: {alive, address(), generation(), {byte(), ip_port()}} |
                 {suspicious, address(), generation(), {}} |
                 {dead, address(), generation(), {}} |
                 {left, address(), generation(), {}}.

-type tick() :: integer().
-type node_states() :: list({address(), node_state()}).
-type awaiting_replies() :: list({address(), tick()}).

-type node_health() :: alive | supicious | dead | left.
-type generation() :: byte().
-type service_type() :: byte().

-type node_state() :: {node_health(), generation(), ({service_type(), ip_port()} | {})}.

-define(LATER_GENERATION(Gen1, Gen2),
        (((0 < (Gen1 - Gen2)) and ((Gen1 - Gen2) < 191))
        or ((Gen1 - Gen2) =< -191))).

-define(SUSPICIOUS_TIMEOUT, 2).
-define(DEATH_TIMEOUT, 8).

later_generation_test_() ->
    [?_assert(?LATER_GENERATION((A + B) rem 255, A))
     || A <- lists:seq(0, 255), B <- lists:seq(1, 64)].

-spec max_generation(byte(), byte()) -> byte().
max_generation(G1, G2)
  when ?LATER_GENERATION(G1, G2) ->
    G1;
max_generation(_, G) ->
    G.

-spec merge_node_state(node_state(), node_state()) -> node_state().
merge_node_state({alive, G, _}, {T, G, E}) ->
    {T, G, E};
merge_node_state({T, G, E}, {alive, G, _}) ->
    {T, G, E};
merge_node_state({left, G, E}, {_, G, _}) ->
    {left, G, E};
merge_node_state({_, G, _}, {left, G, E}) ->
    {left, G, E};
merge_node_state({dead, G, E}, {suspicious, G, _}) ->
    {dead, G, E};
merge_node_state({T, G1, E}, {_, G2, _})
  when ?LATER_GENERATION(G1, G2)->
    {T, G1, E};
merge_node_state(_, {T, G, E}) ->
    {T, G, E}.

-spec process_event(address(), event(), node_states()) -> node_states().
process_event(Us, {alive, Us, _, _}, Nodes) ->
    Nodes;
process_event(Us, {_, Us, Gen, _}, Nodes) ->
    orddict:update(Us,
                   fun({_, G, S}) ->
                           {alive, max_generation(G, (Gen + 1) rem 255), S}
                   end,
                   Nodes);
process_event(_, {Type, Node, Generation, Services}, Nodes) ->
    NodeState = {Type, Generation, Services},
    orddict:update(Node,
                   fun(X) -> merge_node_state(NodeState, X) end,
                   NodeState,
                   Nodes).

-spec shuffle(list()) -> list().
shuffle(L) ->
    RandomlyValued = [ {rand:uniform(), N} || N <- L ],
    [ X || {_,X} <- lists:sort(RandomlyValued)].

-spec parse_event(<<_:64, _:_*8>>) -> {event(), binary()}.
parse_event(<<0:8, A:8, B:8, C:8, D:8, Port:16, Generation:8,
              Service:8, ServicePort:16,
              Rest/binary>>) ->
    {{alive, {{A, B, C, D}, Port}, Generation, {Service, ServicePort}}, Rest};
parse_event(<<1:8, A:8, B:8, C:8, D:8, Port:16, Generation:8,
              Rest/binary>>) ->
    {{suspicious, {{A, B, C, D}, Port}, Generation, {}}, Rest};
parse_event(<<2:8, A:8, B:8, C:8, D:8, Port:16, Generation:8,
              Rest/binary>>) ->
    {{dead, {{A, B, C, D}, Port}, Generation, {}}, Rest};
parse_event(<<3:8, A:8, B:8, C:8, D:8, Port:16, Generation:8,
              Rest/binary>>) ->
    {{left, {{A, B, C, D}, Port}, Generation, {}}, Rest}.

-spec parse_events(binary()) -> [event()].
parse_events(<<>>) ->
    [];
parse_events(Message) ->
    case parse_event(Message) of
        {Event, Rest} ->
            [Event | parse_events(Rest)]
    end.

-spec unparse_event(address(), node_state()) -> <<_:64, _:_*24>>.
unparse_event({{A, B, C, D}, Port}, {alive, Generation, {Service, ServicePort}}) ->
    <<0:8, A:8, B:8, C:8, D:8, Port:16, Generation:8, Service:8, ServicePort:16>>;
unparse_event({{A, B, C, D}, Port}, {suspicious, Generation, _}) ->
    <<1:8, A:8, B:8, C:8, D:8, Port:16, Generation:8>>;
unparse_event({{A, B, C, D}, Port}, {dead, Generation, _}) ->
    <<2:8, A:8, B:8, C:8, D:8, Port:16, Generation:8>>;
unparse_event({{A, B, C, D}, Port}, {left, Generation, _}) ->
    <<3:8, A:8, B:8, C:8, D:8, Port:16, Generation:8>>.

-spec unparse_events([{address(), node_state()}], binary()) -> binary().
unparse_events([], Binary) ->
    Binary;
unparse_events([{Addr, NodeState} | Events], Binary) ->
    EventBinary = unparse_event(Addr, NodeState),
    case byte_size(EventBinary) + byte_size(Binary) =< 508 of
        true -> unparse_events(Events, <<Binary/binary, EventBinary/binary>>);
        false -> Binary
    end.

-spec shuffle_events(address(), node_states()) -> node_states().
shuffle_events(Us, Nodes) ->
    {UsNodes, OtherNodes} = lists:partition(fun({Addr, _}) -> Addr == Us end, Nodes),
    lists:append(UsNodes, shuffle(OtherNodes)).

-spec send(port(), ip_address(), ip_port(), binary()) -> any().
send(Socket, Ip, Port, Message) ->
    case rand:uniform() > 0.1 of
        true -> gen_udp:send(Socket, Ip, Port, Message);
        false -> ok
    end.

-spec send_ack(port(), address(), address(), node_states()) -> any().
send_ack(_, Us, Us, Nodes) ->
    %% if we try to ack ourselves... just don't.
    Nodes;
send_ack(Socket, Us, Address, Nodes) ->
    %% io:format("  ack ~p -> ~p~n", [inet:port(Socket), Address]),
    {Ip, Port} = Address,
    Message = unparse_events(shuffle_events(Us, Nodes), <<0:6, 0:2>>),
    send(Socket, Ip, Port, Message).

-spec send_ping(port(), address(), tick(), address(), node_states(), awaiting_replies()) -> awaiting_replies().
send_ping(_, Us, _, Us, _, AwaitingReplies) ->
    %% if we try to ping ourselves... just don't.
    AwaitingReplies;
send_ping(Socket, Us, Tick, Address, Nodes, AwaitingReplies) ->
    %% io:format("  ping ~p -> ~p~n", [inet:port(Socket), Address]),
    {Ip, Port} = Address,
    Message = unparse_events(shuffle_events(Us, Nodes), <<0:6, 1:2>>),
    send(Socket, Ip, Port, Message),
    case orddict:find(Address, AwaitingReplies) of
        {ok, _} -> AwaitingReplies;
        error -> orddict:store(Address, Tick + ?SUSPICIOUS_TIMEOUT, AwaitingReplies)
    end.

-spec send_indirect_ack(port(), address(), address(), address(), node_states()) -> any().
send_indirect_ack(Socket, Us, Destination, Relay, Nodes) ->
    %% io:format("  indirect-ack ~p -> ~p -> ~p~n", [inet:port(Socket), Relay, Destination]),
    {Ip, Port} = Relay,
    {{DA, DB, DC, DD}, DPort} = Destination,
    {{UA, UB, UC, UD}, UPort} = Us,
    Message = unparse_events(shuffle_events(Us, Nodes),
                             <<0:6, 2:2,
                               DA:8, DB:8, DC:8, DD:8, DPort:16,
                               UA:8, UB:8, UC:8, UD:8, UPort:16>>),
    send(Socket, Ip, Port, Message).

-spec send_indirect_ping(port(), address(), address(), address(), node_states(), awaiting_replies()) -> awaiting_replies().
send_indirect_ping(Socket, Us, Destination, Relay, Nodes, AwaitingReplies) ->
    %% io:format("  indirect-ping ~p -> ~p -> ~p~n", [inet:port(Socket), Relay, Destination]),
    {Ip, Port} = Relay,
    {{DA, DB, DC, DD}, DPort} = Destination,
    {{UA, UB, UC, UD}, UPort} = Us,
    Message = unparse_events(shuffle_events(Us, Nodes),
                             <<0:6, 3:2,
                               DA:8, DB:8, DC:8, DD:8, DPort:16,
                               UA:8, UB:8, UC:8, UD:8, UPort:16>>),
    send(Socket, Ip, Port, Message),
    AwaitingReplies.

-spec process_message(port(), address(), address(), binary(), node_states()) -> node_states().
process_message(_, Us, _, <<0:6, 0:2, EventsBinary/binary>>, Nodes) ->
    %% process an ACK message
    Events = parse_events(EventsBinary),
    lists:foldl(fun (E, Ns) -> process_event(Us, E, Ns) end, Nodes, Events);
process_message(Socket, Us, Sender, <<0:6, 1:2, EventsBinary/binary>>, Nodes) ->
    %% process a PING message
    send_ack(Socket, Us, Sender, Nodes),
    Events = parse_events(EventsBinary),
    lists:foldl(fun (E, Ns) -> process_event(Us, E, Ns) end, Nodes, Events);
process_message(Socket, Us, _, Msg= <<0:6, 2:2,
                DA:8, DB:8, DC:8, DD:8, DPort:16,
                SA:8, SB:8, SC:8, SD:8, SPort:16,
                EventsBinary/binary>>, Nodes) ->
    %% process an indirect ACK message
    case Us of
        {{DA, DB, DC, DD}, DPort} ->
            %% we're the final destination, so process it as an ack
            Events = parse_events(EventsBinary),
            lists:foldl(fun (E, Ns) -> process_event(Us, E, Ns) end, Nodes, Events);
        _ ->
            %% we're not the destination, so forward it to the destination
            send(Socket, {SA, SB, SC, SD}, SPort, Msg),
            Events = parse_events(EventsBinary),
            lists:foldl(fun (E, Ns) -> process_event(Us, E, Ns) end, Nodes, Events)
    end;
process_message(Socket, Us, Sender, Msg= <<0:6, 3:2,
                DA:8, DB:8, DC:8, DD:8, DPort:16,
                SA:8, SB:8, SC:8, SD:8, SPort:16,
                EventsBinary/binary>>, Nodes) ->
    %% process an indirect PING message
    case Us of
        {{DA, DB, DC, DD}, DPort} ->
            %% we're the final destination, so process it as a ping
            send_indirect_ack(Socket, Us, {{SA, SB, SC, SD}, SPort}, Sender, Nodes),
            Events = parse_events(EventsBinary),
            lists:foldl(fun (E, Ns) -> process_event(Us, E, Ns) end, Nodes, Events);
        _ ->
            %% we're not the destination, so forward it to the destination
            send(Socket, {SA, SB, SC, SD}, SPort, Msg),
            Events = parse_events(EventsBinary),
            lists:foldl(fun (E, Ns) -> process_event(Us, E, Ns) end, Nodes, Events)
    end.

-spec expire(node_state()) -> node_state().
expire({alive, G, Ls, E}) ->
    {suspicious, G, Ls, E};
expire({suspicious, G, Ls, E}) ->
    {dead, G, Ls, E};
expire(X) ->
    X.

-spec try_again(port(), address(), integer(), {address(), node_state()}, node_states(), awaiting_replies()) -> awaiting_replies().
try_again(Socket, Us, Tick, {Address, {suspicious, _, _, _}}, Nodes, AwaitingReplies) ->
    %% io:format("trying again: ~p~n", [Address]),
    PingableNodes = lists:filter(fun({Addr, _}) -> (Addr /= Us) and (Addr /= Address) end, Nodes),
    ChosenIndices = lists:sublist(shuffle(lists:seq(1, length(PingableNodes))), 3),
    PingAddress = fun(Index, AwaitingReplies_) ->
                          {Relay, _} = lists:nth(Index, PingableNodes),
                          send_indirect_ping(Socket, Us, Address, Relay, Nodes, AwaitingReplies_)
                  end,
    NewAwaitingReplies = lists:foldl(PingAddress, AwaitingReplies, ChosenIndices),
    % indirect_ping ...
    orddict:store(Address, Tick + ?DEATH_TIMEOUT, NewAwaitingReplies);
try_again(_, _, _, _, _, AwaitingReplies) ->
    AwaitingReplies.

-spec tick(port(), address(), tick(), node_states(), awaiting_replies()) -> {node_states(), awaiting_replies()}.
tick(Socket, Us, Tick, Nodes, RawAwaitingReplies) ->
    AwaitingReplies = lists:filter(fun({Addr, _}) -> orddict:is_key(Addr, Nodes) end, RawAwaitingReplies),
    %% Expire old awaiting records
    {Expired, StillWaiting} = lists:partition(fun({_, TickLimit}) -> TickLimit =< Tick end, AwaitingReplies),
    %% io:format("expired: ~p at tick ~p, nodes: ~p~n", [Expired, Tick, Nodes]),
    ExpiredNodes = lists:map(fun({Address, _}) -> {Address, expire(orddict:fetch(Address, Nodes))} end, Expired),
    NewNodes = orddict:merge(fun(_, _, Y) -> Y end, Nodes, ExpiredNodes),
    NewerAwaitingReplies = lists:foldl(fun(X, Acc) -> try_again(Socket, Us, Tick, X, Nodes, Acc) end, StillWaiting, ExpiredNodes),
    %% here we abuse that fact that orddict is represented as a list
    PingableNodes = lists:filter(fun({Addr, _}) -> Addr /= Us end, Nodes),
    ChosenIndices = lists:sublist(shuffle(lists:seq(1, length(NewNodes))), 3),
    PingAddress = fun(Index, AwaitingReplies_) ->
                          {Address, _} = lists:nth(Index, NewNodes),
                          send_ping(Socket, Us, Tick, Address, PingableNodes, AwaitingReplies_)
                  end,
    { NewNodes,
      lists:foldl(PingAddress, NewerAwaitingReplies, ChosenIndices)
    }.

-spec listener(port(), {ip_address(), ip_port()}, tick(), node_states(), awaiting_replies()) -> any().
listener(Socket, Us, Tick, Nodes, AwaitingReplies) ->
    receive
        tick ->
            {NewNodes, NewAwaitingReplies} = tick(Socket, Us, Tick, Nodes, AwaitingReplies),
            io:format("~p Nodes: ~p~n~n", [Tick, Nodes]),
            listener(Socket, Us, Tick + 1, NewNodes, NewAwaitingReplies);

        {ping, Address} ->
            NewNodes = orddict:store(Address, {dead, 0, {}}, Nodes),
            listener(Socket,
                     Us,
                     Tick,
                     NewNodes,
                     send_ping(Socket, Us, Tick, Address, NewNodes, AwaitingReplies));

        {udp, Socket, FromIp, FromPort, Message} ->
            NewNodes = process_message(Socket, Us, {FromIp, FromPort}, Message, Nodes),
            NewAwaitingReplies = orddict:erase({FromIp, FromPort}, AwaitingReplies),
            listener(Socket,
                     Us,
                     Tick,
                     NewNodes,
                     NewAwaitingReplies);

        report ->
            io:format("Nodes: ~p~n~n", [Nodes]),
            listener(Socket, Us, Tick, Nodes, AwaitingReplies);

        pause ->
            timer:sleep(5000),
            listener(Socket, Us, Tick, Nodes, AwaitingReplies);

        {close, ReplyTo} ->
            io:format("closing ~p:~n    ~p~n    ~p~n", [Us, Nodes, AwaitingReplies]),
            gen_udp:close(Socket),
            ReplyTo ! ok,
            ok;

        _ ->
            unknown_message
    end.

%% -spec local_ip_v4() -> ip_address().
%% local_ip_v4() ->
%%     {ok, Addrs} = inet:getifaddrs(),
%%     hd([
%%          Addr || {_, Opts} <- Addrs, {addr, Addr} <- Opts,
%%          size(Addr) == 4, Addr =/= {127,0,0,1}
%%     ]).

-spec listener(ip_address(), ip_port()) -> any().
listener(Ip, Port) ->
    Address = {Ip, Port},
    {ok, Socket} = gen_udp:open(Port, [binary, {active,true}]),
    timer:send_interval(1000, tick),
    listener(Socket, Address, 0, [{Address, {alive, 0, {1, 80}}}], []).

%% c(gossip).
%% L1 = spawn(gossip, listener, [{127,0,0,1}, 5220]).
%% L1 ! {ping, {{127,0,0,1}, 5221}}.
%% L1 ! {ping, {{127,0,0,1}, 5222}}.

%% c(gossip).
%% gossip:listener({127,0,0,1}, 5221).

%% c(gossip).
%% gossip:listener({127,0,0,1}, 5222).

report_loop(Ls, I) ->
    receive
        report ->
            [_, L2] = Ls,
            %% L ! report,
            case I of
                2 -> L2 ! {close, self()};
                _ -> ok
            end,
            %% hd(shuffle(Lt)) ! pause,
            report_loop(Ls, I + 1)
    end.

start() ->
    Num = 2,
    [L1 | Ls] = [spawn(?MODULE, listener, [{127,0,0,1}, 5250 + X]) || X <- lists:seq(1, Num)],
    [L1 ! {ping, {{127,0,0,1}, 5250 + X}} || X <- lists:seq(2, Num)],
    timer:send_interval(1000, report),
    report_loop([L1 | Ls], 0).
