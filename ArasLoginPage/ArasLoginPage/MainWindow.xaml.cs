using IdentityModel.Client;
using Newtonsoft.Json.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace ArasLoginPage
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {

        string callbackUrl;
        string AuthorizeUrl;
        string accessTokenUrl;
        private readonly HttpListener _httpListener;

        public MainWindow()
        {
            AuthorizeUrl = GenerateUrl.AuthorizeUrl();
            callbackUrl = GenerateUrl.GetCallbackUrl();
            accessTokenUrl = GenerateUrl.GetaccessTokenUrl();
            InitializeComponent();


            _httpListener = new HttpListener();
            _httpListener.Prefixes.Add(callbackUrl + "/");
            _httpListener.Start();
            Task.Run(() => HandleCallback());
            webBrowser.Navigate(AuthorizeUrl);
        }



        private async Task HandleCallback()
        {
            var context = await _httpListener.GetContextAsync();
            var code = context.Request.QueryString["code"];


            string codeverifier = GenerateUrl.GetcodeVerifier();
            HttpClient tokenClient = new HttpClient();


            var tokenResponse = await tokenClient.RequestAuthorizationCodeTokenAsync(new AuthorizationCodeTokenRequest
            {
                Address = accessTokenUrl,//"http://localhost/InnovatorServer/OAuthServer/connect/token",
                ClientId = "InnovatorClient",
                RedirectUri = callbackUrl,
                Code = code,
                CodeVerifier = codeverifier
            }); ;

              MessageBox.Show($"Access Token : {tokenResponse.AccessToken}");


            if (tokenResponse.IsError)
            {
                MessageBox.Show($"Access token request failed: {tokenResponse.Error}");
                return;
            }
            else
            {
                MessageBox.Show($"Login Successfully"); //+"\n"+ $"Access Token : {tokenResponse.AccessToken}");

            }
            context.Response.Close();
            _httpListener.Stop();

            Application.Current.Dispatcher.Invoke(() => Close());

            // Use the access token to make authenticated requests to Aras
            var accessToken = tokenResponse.AccessToken;
            var apiUrl = "http://localhost/InnovatorServer/server/odata/Part?$top=5";




            var getUserInfoTask = RequestAsync(apiUrl, null, accessToken);

            // Handle responses
            // Perform GET request

            // Handle responses
            if (getUserInfoTask.Result != null)
            {
                var userInfoJson = JToken.Parse(getUserInfoTask.Result);
                MessageBox.Show($"\nGET request result (JSON): {userInfoJson}");
            }

           
            // Close the browser window
            // Application.Current.Dispatcher.Invoke(() => Close());
        }



        static async Task<string> RequestAsync(string url, string jsonData = null, string accessToken = null)
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
    }
}