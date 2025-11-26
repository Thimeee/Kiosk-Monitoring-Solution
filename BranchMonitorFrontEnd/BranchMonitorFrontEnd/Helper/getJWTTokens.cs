using System.IdentityModel.Tokens.Jwt;
using BranchMonitorFrontEnd.Models;
using Microsoft.JSInterop;

namespace BranchMonitorFrontEnd.Helper
{
    public class getJWTTokens
    {
        private readonly IJSRuntime _js;

        public getJWTTokens(IJSRuntime js)
        {
            _js = js;
        }
        public async Task<string?> GetJWTLoggedInUserAsync()
        {
            try
            {
                var token = await _js.InvokeAsync<string>("localStorage.getItem", "authToken");

                if (!string.IsNullOrWhiteSpace(token))
                {
                    return token;
                }

                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading JWT token: {ex.Message}");
                return null;
            }
        }

        public async Task<string?> GetJWTLoggedInBranchAsync()
        {
            try
            {
                var token = await _js.InvokeAsync<string>("localStorage.getItem", "branchToken");

                if (!string.IsNullOrWhiteSpace(token))
                {
                    return token;
                }

                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading JWT token: {ex.Message}");
                return null;
            }
        }

    }
}
