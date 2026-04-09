using IdentityModel.Client;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Navigation;
using System.Xml.Linq;


namespace lastchance
{
    internal class GenerateUrl
    {
        // Aras OAuth 2.0 configuration
        
        static string clientId = "InnovatorClient";
        static string callbackUrl = "http://localhost/InnovatorServer/Client/OAuth/PopupCallback";
        static string authUrl = "http://localhost/InnovatorServer/oauthserver/connect/authorize";
        static  string accessTokenUrl = "http://localhost/InnovatorServer/OAuthServer/connect/token";

        // Create PKCE code verifier and challenge
        static string codeVerifier = GenerateCodeVerifier();
        static string codeChallenge = GenerateCodeChallenge(codeVerifier);

        // Create an authorize request
        public static string AuthorizeUrl()
        {

            // Create an authorize request
            var authorizeRequest = new RequestUrl(authUrl);
            var authorizeUrl = authorizeRequest.CreateAuthorizeUrl(
                clientId: clientId,
                responseType: "code",
                scope: "openid Innovator offline_access",
                redirectUri: callbackUrl,
                codeChallenge: codeChallenge,
                codeChallengeMethod: "S256");

            return authorizeUrl;
        }

        public static string GetcodeVerifier()
        {
            return codeVerifier;
        }

        public static string GenerateCodeVerifier()
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

        public static string GetCallbackUrl()
        {
            return callbackUrl;
        }
    }
}
