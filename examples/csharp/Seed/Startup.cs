using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using GossipMesh.Core;
using GossipMesh.Seed.Hubs;
using GossipMesh.Seed.Json;
using GossipMesh.Seed.Listeners;
using GossipMesh.Seed.Stores;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Serialization;

namespace GossipMesh.Seed
{
    public class Startup
    {
        private readonly IConfiguration _configuration;
        public Startup(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public void ConfigureServices(IServiceCollection services)
        {
            services.AddSignalR()
            .AddJsonProtocol(option =>
            {
                option.PayloadSerializerSettings.Converters.Add(new IPAddressConverter());
                option.PayloadSerializerSettings.Converters.Add(new IPEndPointConverter());
                option.PayloadSerializerSettings.Converters.Add(new MemberStateConverter());
                option.PayloadSerializerSettings.Converters.Add(new DateTimeConverter());
            });

            services.AddSingleton<IMemberGraphStore, MemberGraphStore>();
            services.AddSingleton<IMemberEventsStore, MemberEventsStore>();
            services.AddSingleton<IMemberListener, MemberListener>();
        }

        public void Configure(ILogger<Startup> logger, IEnumerable<IMemberListener> memberListeners, IApplicationBuilder app, IHostingEnvironment env)
        {
            app.UseDefaultFiles();
            app.UseStaticFiles();
            app.UseSignalR(routes =>
            {
                routes.MapHub<MembersHub>("/membersHub");
            });

            var listenPort = ushort.Parse(_configuration["port"]);
            var options = new GossiperOptions
            {
                SeedMembers = GetSeedEndPoints(),
                MemberListeners = memberListeners
            };

            var gossiper = new Gossiper(listenPort, 0x01, (ushort)5000, options, logger);
            gossiper.StartAsync().ConfigureAwait(false);
        }

        public IPEndPoint[] GetSeedEndPoints()
        {
            if (_configuration["seeds"] == null)
            {
                return new IPEndPoint[] {};
            }
             return _configuration["seeds"].Split(",")
                .Select(endPoint => endPoint.Split(":"))
                    .Select(endPointValues => new IPEndPoint(IPAddress.Parse(endPointValues[0]), ushort.Parse(endPointValues[1]))).ToArray();
        }
    }
}
