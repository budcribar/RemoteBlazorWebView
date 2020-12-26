using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RemoteableWebWindowService.Services;

namespace RemoteableWebViewService.Pages
{
    public class RestartModel : PageModel
    {
        [Inject]
        public virtual ConcurrentDictionary<string, ServiceState>? Dictionary { get; set; }

        public void OnGet()
        {
            var guid = Request.Query["guid"];
        }
    }
}
