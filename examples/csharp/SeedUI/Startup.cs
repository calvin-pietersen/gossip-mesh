using System.Linq;
using System.Net;
using GossipMesh.Core;
using GossipMesh.SeedUI.Hubs;
using GossipMesh.SeedUI.Json;
using GossipMesh.SeedUI.Stores;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SpaServices.AngularCli;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GossipMesh.SeedUI
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddMvc().SetCompatibilityVersion(CompatibilityVersion.Version_2_1);

            // In production, the Angular files will be served from this directory
            services.AddSpaStaticFiles(configuration =>
            {
                configuration.RootPath = "ClientApp/dist";
            });

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

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IHostingEnvironment env, ILogger<Startup> logger)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseExceptionHandler("/Error");
                app.UseHsts();
            }

            app.UseHttpsRedirection();
            app.UseStaticFiles();
            app.UseSpaStaticFiles();

            app.UseMvc(routes =>
            {
                routes.MapRoute(
                    name: "default",
                    template: "{controller}/{action=Index}/{id?}");
            });

            app.UseSpa(spa =>
            {
                // To learn more about options for serving an Angular SPA from ASP.NET Core,
                // see https://go.microsoft.com/fwlink/?linkid=864501

                spa.Options.SourcePath = "ClientApp";

                if (env.IsDevelopment())
                {
                    spa.UseAngularCliServer(npmScript: "start");
                }
            });

            app.UseSignalR(routes =>
            {
                routes.MapHub<MembersHub>("/membersHub");
            });

            var listenPort = ushort.Parse(Configuration["port"]);
            var options = new GossiperOptions
            {
                SeedMembers = GetSeedEndPoints(),
            };

            var gossiper = new Gossiper(listenPort, 0x01, (ushort)5000, options, logger);
            gossiper.StartAsync().ConfigureAwait(false);
        }

        public IPEndPoint[] GetSeedEndPoints()
        {
            if (Configuration["seeds"] == null)
            {
                return new IPEndPoint[] { };
            }
            return Configuration["seeds"].Split(",")
               .Select(endPoint => endPoint.Split(":"))
                   .Select(endPointValues => new IPEndPoint(IPAddress.Parse(endPointValues[0]), ushort.Parse(endPointValues[1]))).ToArray();
        }
    }
}
