using Microsoft.Graph;
using System;
using System.Threading.Tasks;
using Microsoft.Graph.Models;
using Microsoft.Kiota.Abstractions.Authentication;

namespace OneDriveRipper.Graph
{
    public class GraphHelper
    {
        private readonly GraphServiceClient _graphClient;
        private readonly OneDriveHandler _handler;
        public GraphHelper(IAuthenticationProvider authProvider)
        {
            _graphClient = new GraphServiceClient(authProvider);
            _handler = new OneDriveHandler(_graphClient);
        }

        public async Task GetFilesOneDrive(string path)
        {
            await _handler.GetFiles(path);
        }
        public async Task<User?> GetMeAsync()
        {
            try
            {
                // GET /me
                return await _graphClient.Me
                    .GetAsync(requestConfiguration =>
                    {
                        requestConfiguration.QueryParameters.Select = ["displayName"];
                    });
            }
            catch (ServiceException ex)
            {
                Console.WriteLine($"Error getting signed-in user: {ex.Message}");
                return null;
            }
        }
    }
}