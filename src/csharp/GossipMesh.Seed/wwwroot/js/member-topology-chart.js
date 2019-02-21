var nodes = ['1', '2', '3', '4', '5', '6', '7', '8', '9', '10', '11', '12'];
  var links = [
    {source: 10, target: 11},
    {source: 10, target: 1},
    {source: 10, target: 2},
    {source: 10, target: 3},
    {source: 10, target: 4},
    {source: 10, target: 5},
    {source: 10, target: 6},
    {source: 10, target: 9}
  ];
  var pi = 3.14;
  var r1 = 200;
  var r2 = 20;

  var height = 500;
  var width = 500;
  var cx = height / 2;
  var cy = width / 2;
  var delta = (2 * pi) / 12;

  var circleGroup = d3.select('#svg').append('svg')
      .attr('height', height)
      .attr('width', width)
      .append('g')
      .attr('transform', 'translate(' + cx + ',' + cy + ')');

  circleGroup.append('circle')
      .attr('class', 'big-circle')
      .attr('r', r1);


  circleGroup.selectAll('.little-circle')
      .data(nodes)
      .enter().append('circle')
      .attr('class', 'little-circle')
      .attr('r', r2)
      .attr('cx', getX)
      .attr('cy', getY);

  circleGroup.selectAll('.label')
      .data(nodes)
      .enter().append('text')
      .attr('class', 'label')
      .attr('x', getX)
      .attr('y', getY)
      .text(function(d) { return d; });


  function getX(d) {
    var theta = +d * delta;
    return r1 * Math.cos(theta);
  }

  function getY(d) {
    var theta = +d * delta;
    return r1 * Math.sin(theta);
  }