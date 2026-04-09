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

namespace WpfApp2
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

            string url = "http://localhost/InnovatorServer/"; //"https://myserver/MyInnovator/Server/InnovatorServer.aspx";
            // Step 1: Set up WpfBrowserNavigator
            var navigator = new WpfBrowserNavigator
            {
                Width = 900,
                Height = 600,
                Title = "Innovator",

                // You can set Owner or OwnerHandle properties if needed
            };


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
                Navigator = navigator

            };

            // Step 3: Create AuthorizationFlowTokenProvider
            var tokenProvider = new AuthorizationFlowTokenProvider(options);

            // Step 4: Obtain access token
            try
            {
                string accessToken = await tokenProvider.GetAccessTokenAsync();
                MessageBox.Show(accessToken);
                // Close the Main Window after successful login
                  System.Windows.Application.Current.MainWindow.Close();


            }
            catch (Exception ex)
            {
                // Handle exception
                Console.WriteLine($"An error occurred: {ex.Message}");
            }
        }
    }
}
