var width = 960,
    height = 500,
    border = 1,
    bordercolor = 'black';


var svg = d3.select("body").append("svg")
    .attr("width", width)
    .attr("height", height)
    .attr("border", 1);

svg.append("rect")
    .attr("x", 0)
    .attr("y", 0)
    .attr("height", height)
    .attr("width", width)
    .style("stroke", bordercolor)
    .style("fill", "none")
    .style("stroke-width", border);
