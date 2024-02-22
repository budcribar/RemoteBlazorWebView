using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Components;
using System.Threading.Tasks;

namespace WebdriverTestProject
{
    public partial class Home : ComponentBase
    {
        protected async override Task OnInitializedAsync()
        {
            await Task.Delay(11);
        }

    }
}