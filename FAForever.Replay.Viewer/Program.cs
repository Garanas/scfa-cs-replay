using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

using FAForever.Replay.Viewer.Services;

using MudBlazor.Services;

using Phork.Blazor;
using MudBlazor;

namespace FAForever.Replay.Viewer
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var builder = WebAssemblyHostBuilder.CreateDefault(args);
            
            builder.RootComponents.Add<App>("#app");
            builder.RootComponents.Add<HeadOutlet>("head::after");



            builder.Services.AddScoped(sp => new HttpClient {   });
            builder.Services.AddSingleton<ReplayService>();
            
            builder.Services.AddPhorkBlazorReactivity();
            builder.Services.AddMudServices();


            var app = builder.Build();

            await app.RunAsync();
        }
    }
}
