using IdentityModel.Client;
using IdentityServer4.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace lastchance
{
    internal class GenerateAccessToken
    {

        string codeverifier = GenerateUrl.GenerateCodeVerifier();

        public async Task<string> Token(string authorizationCode)
        {
            HttpClient tokenClient = new HttpClient();

            var tokenResponse = await tokenClient.RequestAuthorizationCodeTokenAsync(new AuthorizationCodeTokenRequest
            {
                Address = "http://localhost/InnovatorServer/OAuthServer/connect/token",
                ClientId = "InnovatorClient",
                RedirectUri = "http://localhost/InnovatorServer/Client/OAuth/PopupCallback",
                Code = authorizationCode,
                CodeVerifier = codeverifier
            });

            return tokenResponse.AccessToken;
        }
    }
}
