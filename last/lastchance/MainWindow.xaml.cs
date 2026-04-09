using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Policy;
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
using IdentityModel.Client;
using IdentityServer4.Models;
using Newtonsoft.Json.Linq;
namespace lastchance
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        

        string callbackUrl;
        string AuthorizeUrl;


        private readonly HttpListener _httpListener;
        public MainWindow()
        {
            AuthorizeUrl= GenerateUrl.AuthorizeUrl();
            callbackUrl = GenerateUrl.GetCallbackUrl();
            InitializeComponent();


            _httpListener = new HttpListener();
            _httpListener.Prefixes.Add(callbackUrl + "/");
            _httpListener.Start();
            Task.Run(() => HandleCallback());
            webBrowser.Navigate(AuthorizeUrl);
            
        }

        

     /*   private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            // Handle navigation to the callback URL here to capture the authorization code
            var authorizationCode = e.Uri.Query; // Extract the authorization code from the URL
            MessageBox.Show($"Received authorization code: {authorizationCode}");
            e.Handled = true;
        }
     */
        private async Task HandleCallback()
        {
            var context = await _httpListener.GetContextAsync();
            var code = context.Request.QueryString["code"];
            MessageBox.Show($"Received authorization code 2: {code}");
            MessageBox.Show($"Login Successfully");

            context.Response.Close();
            _httpListener.Stop();


            string codeverifier = GenerateUrl.GetcodeVerifier();
            HttpClient tokenClient = new HttpClient();

            var tokenResponse = await tokenClient.RequestAuthorizationCodeTokenAsync(new AuthorizationCodeTokenRequest
            {
                Address = "http://localhost/InnovatorServer/OAuthServer/connect/token",
                ClientId = "InnovatorClient",
                RedirectUri = "http://localhost/InnovatorServer/Client/OAuth/PopupCallback",
                Code = code,
                CodeVerifier = codeverifier
            });
          
            MessageBox.Show($"Access Token : {tokenResponse.AccessToken}");

            if (tokenResponse.IsError)
            {
                MessageBox.Show($"Access token request failed: {tokenResponse.Error}");
                return ;
            }
       
            // Use the access token to make authenticated requests to Aras
            var accessToken = tokenResponse.AccessToken;
            var apiUrl = "http://localhost/InnovatorServer/server/odata/Part/$count";




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
            Application.Current.Dispatcher.Invoke(() => Close());
           
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


        /*
        public MainWindow()
        {
            InitializeComponent();
        }

        private void OpenPopup_Click(object sender, RoutedEventArgs e)
        {
            
            string url = "http://localhost/InnovatorServer/oauthserver/Account/Login?ReturnUrl=%2FInnovatorServer%2Foauthserver%2Fconnect%2Fauthorize%2Fcallback%3Fclient_id%3DInnovatorClient%26response_type%3Dcode%26scope%3Dopenid%2520Innovator%2520offline_access%26redirect_uri%3Dhttp%253A%252F%252Flocalhost%252FInnovatorServer%252FClient%252FOAuth%252FPopupCallback%26code_challenge%3D_i-TdiOsGpUeAeHfIMQWnSTFglIQ4Y9HXg1aI18B9HQ%26code_challenge_method%3DS256";
            var popupWindow = new PopupWindow(url);
            popupWindow.ShowDialog();

        }
        */

        private void OpenPopup_Click(object sender, RoutedEventArgs e)
        {

        }
    }
}