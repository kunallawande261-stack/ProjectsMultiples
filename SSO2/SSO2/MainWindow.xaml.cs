
using Aras.IOM;
using Aras.IOM.OAuth;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace SSO2
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            LoginWindow();
        }

        static async void LoginWindow()
        {
            string url = "http://localhost/InnovatorServer34/";
            // Step 1: Set up WpfBrowserNavigator
            var navigator = new WpfBrowserNavigator
            {
                Width = 900,
                Height = 600,
                Title = "Innovator",
            };

            /*
            //Get the Discoverydocumentprovider
            var discoveryDocumentProvider = new DiscoveryDocumentProvider(url);

            DiscoveryDocument discoveryDocument = await discoveryDocumentProvider.GetDiscoveryDocumentAsync();
            
            // Step 2: Set up AuthorizationFlowTokenProviderOptions

            var options = new AuthorizationFlowTokenProviderOptions
            {
                ClientId = "IOMApp",
                DiscoveryDocument = discoveryDocument,
                Scope = "Innovator",
                RedirectUri = "iomapp://token/",
                Prompt = Aras.IOM.OAuth.PromptMode.SelectAccount,             
                Navigator = navigator

            };


            // Step 3: Create AuthorizationFlowTokenProvider
            var tokenProvider = new AuthorizationFlowTokenProvider(options);

            // Step 4: Obtain access token
          
            try
            {
                string accessToken = await tokenProvider.GetAccessTokenAsync();

                Console.WriteLine(accessToken);
               
                // Close the Main Window after successful login
                System.Windows.Application.Current.MainWindow.Close();
            }
            catch (Exception ex)
            {
                // Handle exception
                Console.WriteLine($"An error occurred: {ex.Message}");                
                System.Windows.Application.Current.MainWindow.Close();
            }
            */

            DiscoveryDocumentProvider discoveryDocumentProvider = new DiscoveryDocumentProvider(url);
            DiscoveryDocument discoveryDocument = discoveryDocumentProvider.GetDiscoveryDocumentAsync().GetAwaiter().GetResult();

            var options = new AuthorizationFlowTokenProviderOptions
            {
                ClientId = "IOMApp",
                DiscoveryDocument = discoveryDocument,
                Scope = "Innovator",
                RedirectUri = "iomapp://token/",
                Prompt = Aras.IOM.OAuth.PromptMode.SelectAccount,
                Navigator = navigator

            };

            var tokenProvider = new Aras.IOM.OAuth.AuthorizationFlowTokenProvider(options);

              // string accessToken = await tokenProvider.GetAccessTokenAsync();
            //  await tokenProvider.LoginAsync();
            //  await tokenProvider.LogoutAsync();

               HttpServerConnection conn=IomFactory.CreateHttpServerConnection(url, tokenProvider, discoveryDocument.ProtocolType);
            try
            {
                Item login_result = conn.Login();

                if (login_result.isError())

                    throw new Exception("Login failed");

                Innovator inn = IomFactory.CreateInnovator(conn);

                Item i = inn.newItem("User", "get");
                Item result = i.apply();


                conn.Logout();
            }
            catch (Exception ex)
            {
                // Handle exception
                Console.WriteLine($"An error occurred: {ex.Message}");
                System.Windows.Application.Current.MainWindow.Close();
            }



        }


        
    }


}
