using Aras.IOM.OAuth;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
using System.Threading.Tasks;
using System.Windows;
using Aras.IOM;
using System.Security.Policy;
using System.Windows.Interop;


namespace WpfApp3
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        
        private readonly INavigator _navigator;
        public MainWindow()
        {
            InitializeComponent();

            _navigator = new WpfBrowserNavigator
            {
                Width = 900,
                Height = 600,
                Title = "Innovator",
                Owner = this // Set the owner of the browser window to this WPF window
            };

            GetAccessToken();
        }

        
        private async void GetAccessToken()
        {

            var discoveryDocumentProvider = new DiscoveryDocumentProvider("http://localhost/InnovatorServer/");

            var discoveryDocument = await discoveryDocumentProvider.GetDiscoveryDocumentAsync();


          
           
            // Create the AuthorizationFlowTokenProvider instance
            var tokenProvider = new AuthorizationFlowTokenProvider(
                 new AuthorizationFlowTokenProviderOptions()
                 {
                     DiscoveryDocument = discoveryDocument,
                     ClientId = "IOMApp",
                     RedirectUri = "iomapp://token",
                     Scope = "Innovator",
                     Database = "InnovatorSolutions24backup",
                     AuthenticationType = "Windows",
                     ResponseMode = ResponseMode.Query,
                     Navigator = _navigator
                 });

          
             /*      var tokenProvider = new WindowsTokenProvider(

            new WindowsTokenProviderOptions()

            {

                ClientId = "IOMApp",

                DiscoveryDocument = discoveryDocument,

                RedirectUri = "iomapp://token",

                Scope = "Innovator",

                AuthenticationType = "Windows",

                Database = "InnovatorSolutions24backup"

            });
                */    

            
               HttpServerConnection httpServerConnection = IomFactory.CreateHttpServerConnection("http://localhost/InnovatorServer/Server/InnovatorServer.aspx",

               tokenProvider,

               discoveryDocument.ProtocolType);

               Item login_result = httpServerConnection.Login();

               if (login_result.isError())
               {
                   throw new Exception("Login failed");
               }


               Innovator inn = IomFactory.CreateInnovator(httpServerConnection);

               try
               {
                   // Get the access token asynchronously
                   string accessToken = await tokenProvider.GetAccessTokenAsync();
                   // Use the access token as needed
               }
               catch (Exception ex)
               {
                   // Handle any exceptions
                   MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
               }
            
        }


    }
}
