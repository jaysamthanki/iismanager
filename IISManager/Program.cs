namespace Techie.IISManager
{
    using log4net;
    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.Hosting;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;
    using Microsoft.Extensions.Logging;
    using Microsoft.OpenApi.Models;
    using System;
    using System.IO;
    using System.Reflection;

    /// <summary>
    /// 
    /// </summary>
    public class Program
    {
        /// <summary>
        /// 
        /// </summary>
        /// <param name="args"></param>
        public static void Main(string[] args)
        {
            string log4netConfigFile = "log4net.config";
            var assembly = Assembly.GetEntryAssembly();
            var builder = WebApplication.CreateBuilder(args);

            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")))
            {
                if (File.Exists($"log4net.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")}.config"))
                {
                    log4netConfigFile = $"log4net.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")}.config";
                }
            }

            // AddAsync services to the container.
            builder.Services.AddRazorPages();
            builder.Services.AddMvc();
            builder.Services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new OpenApiInfo { Title = "Techie IIS Manager", Version = "v2" });
                // Set the comments path for the Swagger JSON and UI.
                var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
                var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
                c.IncludeXmlComments(xmlPath);
            });


            builder.Logging
                .AddLog4Net(log4netConfigFile)
                .AddConsole();

            builder.Services.AddHttpLogging(o => { });

            var app = builder.Build();

            // Configure the HTTP request pipeline.
            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Error");
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }
            else
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseHttpsRedirection();
            app.UseStaticFiles();
            app.UseRouting();
            app.UseAuthorization();

            app.MapRazorPages();
            app.MapControllers();
            app.UseSwagger();

            app.UseSwaggerUI(c =>
            {
                c.SwaggerEndpoint("v1/swagger.json", "Techie IIS Manager");
            });

            Global.Log.Info("IIS Manager Started");

            LetsEncryptManager.StartService();

            app.Lifetime.ApplicationStopping.Register(OnShutdown);

            app.Run();
        }

        /// <summary>
        /// 
        /// </summary>
        public static void OnShutdown()
        {
            Global.Log.Info("IIS Manager Stopping");
            LetsEncryptManager.StopService();
            LogManager.Shutdown();
        }
    }
}