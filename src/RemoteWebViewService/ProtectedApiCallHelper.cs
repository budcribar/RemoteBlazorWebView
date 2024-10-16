﻿/*
 The MIT License (MIT)

Copyright (c) 2015 Microsoft Corporation

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
*/

#if AUTHORIZATION
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;

/// <summary>
/// Helper class to call a protected API and process its result
/// </summary>
public class ProtectedApiCallHelper
{
    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="httpClient">HttpClient used to 
    /// call the protected API</param>
    /// <param name="accessToken">Access token used as a bearer 
    /// security token to call the Web API</param>
    public ProtectedApiCallHelper(HttpClient httpClient, string accessToken)
    {
        HttpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        AccessToken = accessToken ?? throw new ArgumentNullException(nameof(accessToken));
    }

    protected HttpClient HttpClient { get; }
    protected string AccessToken { get; }

    /// <summary>
    /// Calls the protected Web API and processes the result
    /// </summary>
    /// <param name="webApiUrl">Url of the Web API to call 
    /// (supposed to return Json)</param>
    /// <param name="processResult">Callback used to process the result 
    /// of the call to the Web API</param>
    public async Task<JObject?> CallWebApiAndProcessResultAsync(string webApiUrl)
    {
        var defaultRequetHeaders = HttpClient.DefaultRequestHeaders;

        if (defaultRequetHeaders.Accept == null || !defaultRequetHeaders.Accept.Any(m => m.MediaType == "application/json"))
        {
            HttpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }
        defaultRequetHeaders.Authorization = new AuthenticationHeaderValue("bearer", AccessToken);

        HttpResponseMessage response = await HttpClient.GetAsync(webApiUrl);
        if (response.IsSuccessStatusCode)
        {
            string json = await response.Content.ReadAsStringAsync();
            JObject? result = JsonConvert.DeserializeObject<JObject>(json);
            return result;
        }
        else
        {
            string content = await response.Content.ReadAsStringAsync();

            // Note that if you got reponse.Code == 403 
            // and reponse.content.code == "Authorization_RequestDenied"
            // this is because the tenant admin as not granted 
            // consent for the application to call the Web API
            Console.WriteLine($"Content: {content}");
            return null;
        }
    }
} 

#endif