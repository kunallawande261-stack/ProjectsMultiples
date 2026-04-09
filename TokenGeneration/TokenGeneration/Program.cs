using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using IdentityModel.Client;
using Newtonsoft.Json.Linq;
using System.Diagnostics;

class Program
{
    static async Task Main(string[] args)
    {
        // Aras OAuth 2.0 configuration
        var clientId = "InnovatorClient";
        var callbackUrl = "http://localhost/InnovatorServer/Client/OAuth/PopupCallback";
        var authUrl = "http://localhost/InnovatorServer/oauthserver/connect/authorize";
        var accessTokenUrl = "http://localhost/InnovatorServer/OAuthServer/connect/token";

        // Create PKCE code verifier and challenge
        var codeVerifier = "bsfzVi9fuijuG8dRR5gU6luhYo7BSIbhtCVRbsMSPWo";//GenerateCodeVerifier(); //"WqzodIic9wjcgXeRoGtXiaVYvJZjpEu33kIED2C0EwmB";/
        var codeChallenge = "S3HVNAiOVb_T930KY3fQR7xtQ-XT4CNcaSKI7eYDUaI";//GenerateCodeChallenge(codeVerifier);


        Console.WriteLine("code verifier:"+ codeVerifier);
        Console.WriteLine("code challange :"+ codeChallenge);
        // Create an HTTP listener to receive the callback
        var listener = new HttpListener();
        listener.Prefixes.Add(callbackUrl + "/");
        listener.Start();

        // Create an authorize request
        var authorizeRequest = new RequestUrl(authUrl);
        var authorizeUrl = authorizeRequest.CreateAuthorizeUrl(
            clientId: clientId,
            responseType: "code",
            scope: "openid Innovator offline_access",
            redirectUri: callbackUrl,
            codeChallenge: codeChallenge,
            codeChallengeMethod: "S256");

        Console.WriteLine("Please visit this URL to authorize the application:");

        // Open the authorization URL in the incognito mode of the Chrome browser
        OpenUrlInDefaultBrowser(authorizeUrl);

        // Handle the authorization code callback
        var context = await listener.GetContextAsync();
        var request = context.Request;
        var response = context.Response;

        if (request.HttpMethod == "GET" && request.Url.AbsolutePath == "/InnovatorServer/Client/OAuth/PopupCallback")
        {
            var authorizationCode = request.QueryString["code"];
            Console.WriteLine($"Received authorization code: {authorizationCode}");
            Console.WriteLine("\n!!! Please close the Tab !!!!");

            // Exchange authorization code for access token
            var tokenClient = new HttpClient();
            var tokenResponse = await tokenClient.RequestAuthorizationCodeTokenAsync(new AuthorizationCodeTokenRequest
            {
                Address = accessTokenUrl,
                ClientId = clientId,
                RedirectUri = callbackUrl,
                Code = authorizationCode,
                CodeVerifier = codeVerifier
            });

            Console.WriteLine($"Access token : {tokenResponse.AccessToken}");

            if (tokenResponse.IsError)
            {
                Console.WriteLine($"Access token request failed: {tokenResponse.Error}");
                return;
            }

            
            // Use the access token to make authenticated requests to Aras
            var accessToken = tokenResponse.AccessToken;
            var apiUrl = "http://localhost/InnovatorServer/server/odata/Part";

            // Define the JSON data to be sent in the request body for POST request
            var jsonData = @"{
                 ""item_number"": ""P-9874996"",
                 ""name"": ""Break sample Part4"",
                }";

            // Perform POST request
            
            var postUserInfoTask = RequestAsync(apiUrl, jsonData, accessToken);  
            var getUserInfoTask = RequestAsync(apiUrl, null, accessToken);
            var logUserInfoTask = RequestAsync("http://localhost/InnovatorServer/server/odata/method.LogOff", "", accessToken);
            // Wait for both GET and POST requests to complete
            // await Task.WhenAll(getUserInfoTask, postUserInfoTask);

            // Handle responses

            if (postUserInfoTask.Result != null)
            {
                var userInfoJson = JToken.Parse(postUserInfoTask.Result);
                Console.WriteLine($"POST request result (JSON): {userInfoJson}");

            }

            // Perform GET request
                       
              // Handle responses
            if (getUserInfoTask.Result != null)
            {
                var userInfoJson = JToken.Parse(getUserInfoTask.Result);
                Console.WriteLine($"\nGET request result (JSON): {userInfoJson}");
            }

            if (logUserInfoTask.Result != null)
            {
                var userInfoJson = JToken.Parse(logUserInfoTask.Result);
                Console.WriteLine($"\nGET request result (JSON): {userInfoJson}");
            }

            // Close the listener
            response.StatusCode = 200;
            response.Close();
        }

       
        // Stop the listener
        listener.Stop();
        OpenUrlInDefaultBrowser("http://localhost/InnovatorServer/OAuthServer/connect/endsession");
        //OpenUrlInDefaultBrowser("http://localhost/InnovatorServer/Client/OAuth/PostLogoutCallback");
    }

    static async Task<string> RequestAsync(string url, string jsonData = null, string accessToken=null)
    {
        using (var httpClient = new HttpClient())
        {
            // Add authorization header
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            

            HttpResponseMessage response;
            if (jsonData == null)
            {
                // Send GET request
                response = await httpClient.GetAsync(url);

            }
            else
            {
                // Define the request content as JSON
                var content = new StringContent(jsonData, Encoding.UTF8, "application/json");
                // Send POST request
                response = await httpClient.PostAsync(url, content);
            }



            // Check if request was successful
            if (response.IsSuccessStatusCode)
            {
                // Read response content
                return await response.Content.ReadAsStringAsync();
            }
            else
            {
                Console.WriteLine("\n!!!!Error in Request url {0}:  status code {1}  ({2})", url, (int)response.StatusCode, response.ReasonPhrase);
                // Handle error response
                return await response.Content.ReadAsStringAsync(); ;
            }
        }
    }

  

    static string GenerateCodeVerifier()
    {
        using (var rng = RandomNumberGenerator.Create())
        {
            byte[] bytes = new byte[32];
            rng.GetBytes(bytes);
            return Convert.ToBase64String(bytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }
    }

    static string GenerateCodeChallenge(string codeVerifier)
    {
        using (var sha256 = SHA256.Create())
        {
            byte[] challengeBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(codeVerifier));
            return Base64UrlEncode(challengeBytes);
        }
    }

    static string Base64UrlEncode(byte[] data)
    {
        return Convert.ToBase64String(data)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    static void OpenUrlInDefaultBrowser(string url)
    {
        try
        {
            // Open the URL in a new browser window
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true, WindowStyle = ProcessWindowStyle.Normal });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error opening URL in default browser: {ex.Message}");
        }
    }

}
