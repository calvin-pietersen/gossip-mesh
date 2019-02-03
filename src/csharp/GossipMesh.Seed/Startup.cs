using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using GossipMesh.Core;
using GossipMesh.Seed.Hubs;
using GossipMesh.Seed.Listeners;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Serialization;

namespace GossipMesh.Seed
{
    public class Startup
    {
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddSignalR(options => {
                options.EnableDetailedErrors = true;
            })
                .AddJsonProtocol(options => {
                    options.PayloadSerializerSettings.ContractResolver = 
                    new DefaultContractResolver();
                });

            services.AddSingleton<IStateListener, MembersListener>();
        }

        public void Configure(ILogger<Startup> logger, IStateListener stateListener, IApplicationBuilder app, IHostingEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseDefaultFiles();
            app.UseStaticFiles();
            app.UseSignalR(routes => {
                routes.MapHub<MembersHub>("/membersHub");
            });

            var options = new GossiperOptions
            {
                MaxUdpPacketBytes = 508,
                ProtocolPeriodMilliseconds = 200,
                AckTimeoutMilliseconds = 80,
                NumberOfIndirectEndpoints = 2,
                ListenPort = (ushort)10010,
                MemberIP = IPAddress.Parse("192.168.1.104"),
                Service = (byte)1,
                ServicePort = (ushort)5000,
                SeedMembers = new IPEndPoint[] { new IPEndPoint(IPAddress.Parse("192.168.1.104"), 10001)},
                StateListeners = new IStateListener[] { stateListener }
            };

            var gossiper = new Gossiper(options, logger);

            gossiper.Start();
        }
    }
}
