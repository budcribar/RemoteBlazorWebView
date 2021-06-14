//using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using System.Threading.Tasks;

namespace Photino.Blazor
{
    public static class JSInteropMethods
    {
        [JSInvokable(nameof(DispatchEvent))]
        public static async Task DispatchEvent(WebEventDescriptor eventDescriptor, string eventArgsJson)
        {
            
            var renderer = ComponentsDesktop.DesktopRenderer;
            if (renderer == null) return;
            var webEvent = WebEventData.Parse(renderer,eventDescriptor, eventArgsJson);
            await renderer.DispatchEventAsync(
                webEvent.EventHandlerId,
                webEvent.EventFieldInfo,
                webEvent.EventArgs);
        }

        [JSInvokable(nameof(NotifyLocationChanged))]
        public static void NotifyLocationChanged(string uri, bool isInterceptedLink)
        {
            DesktopNavigationManager.Instance.SetLocation(uri, isInterceptedLink);
        }

        [JSInvokable(nameof(OnRenderCompleted))]
        public static async Task OnRenderCompleted(long renderId, string errorMessageOrNull)
        {
            var renderer = ComponentsDesktop.DesktopRenderer;
            if (renderer == null) return;
            await renderer.OnRenderCompletedAsync(renderId, errorMessageOrNull);
        }
    }
}
