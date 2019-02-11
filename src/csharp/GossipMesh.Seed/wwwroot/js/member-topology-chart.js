var topo = null,  // keep track of topo.
    animated = false;   // animated or static
function initialize_topo() {
    /*
        create container for links and nodes elements.
    */
    var svg = d3.select('svg#topo_container');
    var links = svg.select('g.links');
    if (!links.size()) {
        links = svg.append('g').attr('class', 'links');
    }
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
        var link_frc = d3.forceLink()
            .id(function (d) {
                return d.id;
            })
            .distance(function (d) {
                if ('id' in d.source && 'id' in d.target) {
                    return 120;
                }
                else {
                    return 60;
                }
            }),
            charge_frc = d3.forceManyBody()
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
            links.attr('transform', d3.event.transform);
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
        var link_src_tip = d3.tip().attr('class', 'tooltip'),
            link_dst_tip = d3.tip().attr('class', 'tooltip');
        svg.call(link_src_tip);
        svg.call(link_dst_tip);
        if (animated) {
            simulation.force('link', link_frc)
                .force('center', center_frc)
                .force('charge', charge_frc);
        }
        // keep track of topo components.
        topo = {
            'simulation': simulation,
            'link_force': link_frc,
            'charge_force': charge_frc,
            'center_force': center_frc,
            'drag': drag,
            'zoom': zoom,
            'node_tip': node_tip,
            'link_source_tip': link_src_tip,
            'link_dest_tip': link_dst_tip
        };
    }
}
function load(graph) {
    /*
        load: load new nodes, links to simulation.
    */
    var simulation = topo['simulation'],
        link_frc = topo['link_force'],
        drag = topo['drag'],
        node_tip = topo['node_tip'],
        link_src_tip = topo['link_source_tip'],
        link_dst_tip = topo['link_dest_tip'],
        svg = d3.select('svg#topo_container');

    var node_by_id = d3.map(graph.nodes, function (d) {
        return d.id;
    });
    var bilinks = [];
    graph.links.forEach(function (link, idx) {
        var src = link.source = node_by_id.get(link.source),
            target = link.target = node_by_id.get(link.target),
            inter = {};
        graph.nodes.push(inter);
        graph.links.push({ 'source': src, 'target': inter }, { 'source': inter, 'target': target });
        bilinks.push({
            'id': link['id'],
            'source': src,
            'intermediate': inter,
            'target': target,
            'source_port': link['source'],
            'target_port': link['target']
        });
    });
    /*
        update link visualization
    */
    var link = svg.select('g.links').selectAll('g.link_container').data(bilinks);
    link.exit().remove();
    var new_link = link.enter()
        .append('g')
        .classed('link_container', true);
    new_link.append('path')
        .classed('link_item', true);
    new_link.append('path')
        .classed('link_selector', true)
        .on('mouseover', function (d) {
            var src_node, dst_node;
            /*
                focus on target link
            */
            d3.select(this.parentNode).classed('focus', true);
            /*
                focus on target and source node
                and show tips.
            */
            svg.select('g.nodes').selectAll('g.node_container')
                .each(function (node_d) {
                    if (node_d.id == d.source.id) {
                        src_node = d3.select(this).classed('focus focusing', true);
                    }
                    else if (node_d.id == d.target.id) {
                        dst_node = d3.select(this).classed('focus focusing', true);
                    }
                });
            /*
                calculate tooltip position
            */
            var src_dir, dst_dir,
                src_offset = [0, 0],
                dst_offset = [0, 0],
                min_distance = 20,
                x_distance = src_node.datum().x - dst_node.datum().x,
                y_distance = src_node.datum().y - dst_node.datum().y;
            if (Math.abs(x_distance) > Math.abs(y_distance)) {
                if (x_distance > 0) {
                    src_dir = 'e';
                    src_offset[1] = 5;
                    dst_dir = 'w';
                    dst_offset[1] = -5;
                }
                else {
                    src_dir = 'w';
                    src_offset[1] = -5;
                    dst_dir = 'e';
                    dst_offset[1] = 5;
                }
                if (Math.abs(y_distance) > min_distance) {
                    if (y_distance > 0) {
                        src_dir = 's' + src_dir;
                        src_offset = [-5, -(Math.sign(src_offset[1]) * 5)];
                        dst_dir = 'n' + dst_dir;
                        dst_offset = [5, -(Math.sign(dst_offset[1]) * 5)];
                    }
                    else {
                        src_dir = 'n' + src_dir;
                        src_offset = [5, -(Math.sign(src_offset[1]) * 5)];
                        dst_dir = 's' + dst_dir;
                        dst_offset = [-5, -(Math.sign(dst_offset[1]) * 5)];
                    }
                }
            }
            else {
                if (y_distance > 0) {
                    src_dir = 's';
                    src_offset[0] = 5;
                    dst_dir = 'n';
                    dst_offset[0] = -5;
                }
                else {
                    src_dir = 'n';
                    src_offset[0] = -5;
                    dst_dir = 's';
                    dst_offset[0] = 5;
                }
                if (Math.abs(x_distance) > min_distance) {
                    if (x_distance > 0) {
                        src_dir = src_dir + 'e';
                        src_offset = [-(Math.sign(src_offset[0]) * 5), -5];
                        dst_dir = dst_dir + 'w';
                        dst_offset = [-(Math.sign(dst_offset[0]) * 5), 5];
                    }
                    else {
                        src_dir = src_dir + 'w';
                        src_offset = [-(Math.sign(src_offset[0]) * 5), 5];
                        dst_dir = dst_dir + 'e';
                        dst_offset = [-(Math.sign(dst_offset[0]) * 5), -5];
                    }
                }
            }
            link_src_tip
                .direction(src_dir)
                .offset(src_offset)
                .html("<strong>" + d.source.state + "</strong>")
                .show(src_node.node());
            link_dst_tip
                .direction(dst_dir)
                .offset(dst_offset)
                .html("<strong> " + d.target.state + "</strong>")
                .show(dst_node.node());
        })
        .on('mouseout', function (d) {
            /*
                move focus away from link.
            */
            d3.select(this.parentNode).classed('focus', false);
            /*
                move focus away from target and source nodes
                hide tips
            */
            svg.select('g.nodes').selectAll('g.node_container')
                .each(function (node_d) {
                    if (node_d.id == d.source.id) {
                        src_node = d3.select(this).classed('focus focusing', false);
                        link_src_tip.hide();
                    }
                    else if (node_d.id == d.target.id) {
                        dst_node = d3.select(this).classed('focus focusing', false);
                        link_dst_tip.hide();
                    }
                });
        });
    link = new_link.merge(link);
    /*
        update node visualization
    */

    var node = svg.select('g.nodes').selectAll('g.node_container').data(
        graph.nodes.filter(function (d) {
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
        // var stat_cls;
        // if (d['knmp_on'] && d['ip_on'] && d['snmp_on']) {
        //     stat_cls = 'stat_normal';
        // }
        // else if (d['knmp_on'] && d['ip_on'] && !d['snmp_on']) {
        //     stat_cls = 'stat_abnormal';
        // }
        // else if (d['knmp_on'] && !d['ip_on'] && !d['snmp_on']) {
        //     stat_cls = 'stat_error'
        // }
        // else if (!d['knmp_on'] && !d['ip_on'] && !d['snmp_on']) {
        //     stat_cls = 'stat_down';
        // }
        // else {
        //     // knmp off, snmp_on -> unknown device.
        //     stat_cls = 'stat_unknown';
        // }
        return 'node_container stat_normal';
    });
    /*
        update descriptions
    */
    var desc = svg.select('g.desc').selectAll('g.desc_container').data(
        graph.nodes.filter(function (d) {
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

    simulation.nodes(graph.nodes);
    link_frc.links(graph.links);

    simulation.on('tick', function () {
        do_tick(link, node, desc);
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
    if (animated) {
        do_animated_layout();
    }
    else {
        do_static_layout();
    }
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
        charge_frc = topo['charge_force'],
        link_frc = topo['link_force'];
    if (!animated) {
        simulation
            .force('center', center_frc)
            .force('charge', charge_frc)
            .force('link', link_frc);
    }
    simulation.alpha(1);
    for (var i = 0, n = Math.ceil(Math.log(simulation.alphaMin()) / Math.log(1 - simulation.alphaDecay())); i < n; ++i) {
        simulation.tick();
    }
    if (!animated) {
        simulation
            .force('center', null)
            .force('charge', null)
            .force('link', null);
    }
    do_one_tick();
}
function do_animated_layout() {
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
        charge_frc = topo['charge_force'],
        link_frc = topo['link_force'];
    if (!animated) {
        simulation
            .force('center', center_frc)
            .force('charge', charge_frc)
            .force('link', link_frc);
    }
    simulation.alpha(1)
    var ticks_per_render = 5;
    window.requestAnimationFrame(function render() {
        for (var i = 0; i < ticks_per_render; i++) {
            simulation.tick();
        }
        do_one_tick();
        if (simulation.alpha() > simulation.alphaMin()) {
            window.requestAnimationFrame(render);
        }
        else {
            if (!animated) {
                simulation
                    .force('center', null)
                    .force('charge', null)
                    .force('link', null);
            }
        }
    });
}
function do_tick(link_sel, node_sel, desc_sel) {
    /*
        update visualization of links
            path
    */
    link_sel.select('path.link_item').attr("d", function (d) {
        return "M" + d['source'].x + "," + d['source'].y
            + "S" + d['intermediate'].x + "," + d['intermediate'].y
            + " " + d['target'].x + "," + d['target'].y;
    });
    link_sel.select('path.link_selector').attr("d", function (d) {
        return "M" + d['source'].x + "," + d['source'].y
            + "S" + d['intermediate'].x + "," + d['intermediate'].y
            + " " + d['target'].x + "," + d['target'].y;
    });
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
        link = svg
            .select('g.links')
            .selectAll('g.link_container'),
        node = svg
            .select('g.nodes')
            .selectAll('g.node_container'),
        desc = svg
            .select('g.desc')
            .selectAll('g.desc_container');
    do_tick(link, node, desc);
}
d3.select('button#load_btn').on('click', function () {
    load();
});
d3.select('button#animated_btn').on('click', function () {
    var me = d3.select(this);
    animated = !animated;
    if (animated) {
        topo['simulation']
            .force('center', topo['center_force'])
            .force('charge', topo['charge_force'])
            .force('link', topo['link_force']);
        do_animated_layout();
        me.text('Animated');
    }
    else {
        topo['simulation']
            .force('center', null)
            .force('charge', null)
            .force('link', null);
        me.text('Static');
    }
});
