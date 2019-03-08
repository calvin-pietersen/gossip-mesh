var topo = null;  // keep track of topo.
function initialize_topo() {
    /*
        create container for nodes elements.
    */
    var svg = d3.select('svg#topo_container');
    var nodes = svg.select('g.nodes');
    if (!nodes.size()) {
        nodes = svg.append('g').attr('class', 'nodes');
    }
    var descs = svg.select('g.desc');
    if (!descs.size()) {
        descs = svg.append('g').attr('class', 'desc');
    }
    if (!topo) {
        /*
            force simulation
        */
        var simulation = d3.forceSimulation().stop();
        /*
            force
        */
        var charge_frc = d3.forceManyBody()
                .strength(function (d) {
                    if ('id' in d) {
                        return -160;
                    }
                    else {
                        return -200;
                    }
                }).distanceMax(300),
            center_frc = d3.forceCenter();
        /*
            gestures.
        */
        // drag node
        var drag = d3.drag()
            .on('start', function (d) {
                if (!d3.event.active) {
                    simulation.alphaTarget(0.3).restart();
                }
                d.fx = d.x;
                d.fy = d.y;
            })
            .on('drag', function (d) {
                d.fx = d3.event.x;
                d.fy = d3.event.y;
            })
            .on('end', function (d) {
                var simulation = topo['simulation'];
                if (!d3.event.active) {
                    simulation.alphaTarget(0);
                }
                d.fx = null;
                d.fy = null;
            });
        // zoom and drag to move
        var zoom = d3.zoom().scaleExtent([0.1, 5]).on('zoom', function () {
            nodes.attr('transform', d3.event.transform);
            descs.attr('transform', d3.event.transform);
        });
        svg.call(zoom);
        /*
            tooltip
        */
        var node_tip = d3.tip()
            .attr('class', 'tooltip')
            .offset([-10, 0])
            .html(function (d) {
                return "<p><strong class='title'>IP:</strong>" + d.ip + "</p>" +
                    "<p><strong class='title'>State:</strong>" + d.state + "</p>" +
                    "<p><strong class='title'>Gen:</strong>" + d.generation + "</p>" +
                    "<p><strong class='title'>Service:</strong>" + d.service + "</p>" +
                    "<p><strong class='title'>Port:</strong>" + d.servicePort + "</p>";
            });
        svg.call(node_tip);

        // keep track of topo components.
        topo = {
            'simulation': simulation,
            'charge_force': charge_frc,
            'center_force': center_frc,
            'drag': drag,
            'zoom': zoom,
            'node_tip': node_tip,
        };
    }
}
function load(nodes) {
    /*
        load: load new nodes to simulation.
    */
    var simulation = topo['simulation'],
        drag = topo['drag'],
        node_tip = topo['node_tip'],
        svg = d3.select('svg#topo_container');

    /*
        update node visualization
    */

    var node = svg.select('g.nodes').selectAll('g.node_container').data(
        nodes.filter(function (d) {
            return 'id' in d;
        })
    );
    node.exit().remove();
    var new_node = node.enter()
        .append('g')
        .on('click', function () { console.log('click:', this); })
        .on('mouseover', function (d) {
            d3.select(this).classed('focus', true);
            return node_tip.show.apply(this, arguments);
        })
        .on('mouseout', function (d) {
            d3.select(this).classed('focus', false);
            return node_tip.hide.apply(this, arguments);
        })
        .call(drag);
    new_node.append('circle')
        .attr('r', 20);
    new_node.append('text')
        .attr('x', 0)
        .attr('y', 12)
        .classed('ftstcall', true)
        .text("\ueaf2");
    node = new_node.merge(node);
    node.attr('class', function (d) {
        return 'node_container ' + d['state'].toLowerCase();
    });
    /*
        update descriptions
    */
    var desc = svg.select('g.desc').selectAll('g.desc_container').data(
        nodes.filter(function (d) {
            return 'id' in d;
        })
    );
    desc.exit().remove();
    var new_desc = desc.enter()
        .append('g')
        .classed('desc_container', true);
    new_desc.append('text')
        .attr('x', 0)
        .attr('y', 35);
    desc = new_desc.merge(desc);
    desc.select('text').text(function (d) {
        return d['id'];
    });

    simulation.nodes(nodes);

    simulation.on('tick', function () {
        do_tick(node, desc);
    });
    do_layout();
}
function do_layout() {
    var svg = d3.select('svg#topo_container'),
        center_frc = topo['center_force'];
    var width = +(svg.style('width').replace('px', '')),
        height = +(svg.style('height').replace('px', ''));
    center_frc.x(width / 2)
        .y(height / 2);
        do_static_layout();
}
function do_static_layout() {
    /*
        deregister drag event.
        register force
        call simulation.tick() several times
        call ticked()   -> draw finished layout
        deregister force
        register drag event again.
    */
    var simulation = topo['simulation'],
        center_frc = topo['center_force'],
        charge_frc = topo['charge_force'];

        simulation
            .force('center', center_frc)
            .force('charge', charge_frc)
    simulation.alpha(1);
    for (var i = 0, n = Math.ceil(Math.log(simulation.alphaMin()) / Math.log(1 - simulation.alphaDecay())); i < n; ++i) {
        simulation.tick();
    }
    simulation
        .force('center', null)
        .force('charge', null)
    do_one_tick();
}
function do_tick(node_sel, desc_sel) {
    /*
        update visualization of nodes
            g <- text
    */
    node_sel.attr('transform', function (d) {
        return 'translate(' + d.x + ',' + d.y + ')';
    });
    /*
        update visualization of description
            g<-text
    */
    desc_sel.attr('transform', function (d) {
        return 'translate(' + d.x + ',' + d.y + ')';
    });
}
function do_one_tick() {
    /*
        handle one tick on graphic elements.
    */
    var svg = d3.select('svg#topo_container'),
        node = svg
            .select('g.nodes')
            .selectAll('g.node_container'),
        desc = svg
            .select('g.desc')
            .selectAll('g.desc_container');
    do_tick(node, desc);
}