using System.Data;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Principal;
using BranchMonitorFrontEnd.DTO;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;

namespace BranchMonitorFrontEnd.Service.Auth
{
    public class AuthCredential
    {

        private readonly IJSRuntime _js;

        public AuthCredential(IJSRuntime js)
        {
            _js = js;
        }
        public async Task<LoggedInUser> getLogginUserDetiles()
        {
            var respone = new LoggedInUser();
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
                        var username = jwt.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Sub)?.Value;
                        var userId = jwt.Claims.FirstOrDefault(c => c.Type == "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier")?.Value;
                        var role = jwt.Claims.FirstOrDefault(c => c.Type == "http://schemas.microsoft.com/ws/2008/06/identity/claims/role")?.Value;

                        respone.UserName = username;
                        respone.UserId = userId;
                        respone.Role = role;
                        respone.status = true;
                    }
                    else
                    {
                        await _js.InvokeVoidAsync("localStorage.removeItem", "authToken");
                        respone.status = false;

                    }
                    // Get individual claims
                }
            }
            catch (Exception ex)
            {
                //Console.WriteLine($"Error loading auth state: {ex.Message}");
                respone.Ex = $"Error loading auth state: {ex.Message}";
                respone.status = false;

            }

            return respone;
        }



        public async Task<LogInBranchDeties> getLogginBranchDetiles()
        {
            var respone = new LogInBranchDeties();
            try
            {
                var token = await _js.InvokeAsync<string>("localStorage.getItem", "branchToken");

                if (!string.IsNullOrWhiteSpace(token))
                {
                    var handler = new JwtSecurityTokenHandler();
                    var jwt = handler.ReadJwtToken(token);

                    // Optional: check expiry
                    if (jwt.ValidTo > DateTime.UtcNow)
                    {
                        var terminalId = jwt.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Sub)?.Value;
                        var branchId = jwt.Claims.FirstOrDefault(c => c.Type == "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier")?.Value;

                        respone.BranchID = branchId;
                        respone.terminalId = terminalId;
                        respone.status = true;
                    }
                    else
                    {
                        await _js.InvokeVoidAsync("localStorage.removeItem", "branchToken");
                        respone.status = false;

                    }
                    // Get individual claims
                }
            }
            catch (Exception ex)
            {
                //Console.WriteLine($"Error loading auth state: {ex.Message}");
                respone.Ex = $"Error loading BranchStatus: {ex.Message}";
                respone.status = false;

            }

            return respone;
        }

        public async Task BranchLogOut()
        {
            try
            {
                await _js.InvokeVoidAsync("localStorage.removeItem", "branchToken");
            }
            catch (Exception ex)
            {
            }

        }
        public async Task UserLogOut()
        {
            try
            {
                var userstate = new CustomAuthStateProvider(_js);
                await userstate.UserLogOut();

            }
            catch (Exception ex)
            {
            }

        }


    }


}
