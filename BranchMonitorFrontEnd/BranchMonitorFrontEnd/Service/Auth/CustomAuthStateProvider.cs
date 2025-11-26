using System.Data;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Principal;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;
using static BranchMonitorFrontEnd.Service.Auth.CustomAuthStateProvider;

namespace BranchMonitorFrontEnd.Service.Auth
{
    public class CustomAuthStateProvider : AuthenticationStateProvider
    {
        private readonly IJSRuntime _js;

        public CustomAuthStateProvider(IJSRuntime js)
        {
            _js = js;
        }

        public override async Task<AuthenticationState> GetAuthenticationStateAsync()
        {
            ClaimsIdentity identity = new ClaimsIdentity();

            try
            {
                var token = await _js.InvokeAsync<string>("localStorage.getItem", "authToken");

                if (!string.IsNullOrWhiteSpace(token))
                {
                    var handler = new JwtSecurityTokenHandler();
                    var jwt = handler.ReadJwtToken(token);

                    // Optional: check expiry
                    if (jwt.ValidTo > DateTime.UtcNow)
                    {
                        identity = new ClaimsIdentity(jwt.Claims, "jwt");
                    }
                    else
                    {
                        await _js.InvokeVoidAsync("localStorage.removeItem", "authToken");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading auth state: {ex.Message}");
            }

            var user = new ClaimsPrincipal(identity);
            return new AuthenticationState(user);
        }

        public async Task UserLogOut()
        {
            try
            {
                await _js.InvokeVoidAsync("localStorage.removeItem", "authToken");
                var anonymousUser = new ClaimsPrincipal(new ClaimsIdentity());
                NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(anonymousUser)));
            }
            catch (Exception ex)
            {
            }

        }



        public void NotifyAuthenticationStateChanged()
        {
            NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
        }
    }
}
