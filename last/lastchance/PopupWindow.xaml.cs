using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace lastchance
{
    /// <summary>
    /// Interaction logic for PopupWindow.xaml
    /// </summary>
    public partial class PopupWindow : Window
    {
        public string AuthorizationCode { get; private set; }
        
        
        public PopupWindow(string url)
        {
            InitializeComponent();
            webBrowser.Navigate(url);
           
        }


        /*
        private void WebBrowser_Navigated(object sender, System.Windows.Navigation.NavigationEventArgs e)
        {
            // Detect if the URL contains the authorization code
            if (Regex.IsMatch(e.Uri.AbsoluteUri, "code=[^&]+"))
            {
                // Extract the authorization code from the URL
                var match = Regex.Match(e.Uri.AbsoluteUri, "code=([^&]+)");
                AuthorizationCode = match.Groups[1].Value;

                // Close the popup window
                Close();
            }
        }*/
    }
}
