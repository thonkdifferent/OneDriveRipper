using Microsoft.Graph;
using System;
using System.Threading.Tasks;
namespace OneDriveRipper.Graph
{
    public class GraphHelper
    {
        private static GraphServiceClient _graphClient;
        public static void Initialize(IAuthenticationProvider authProvider)
        {
            _graphClient = new GraphServiceClient(authProvider);
        }

        public static async Task GetFilesOneDrive(string path)
        {
            await OneDriveHandler.GetFiles(_graphClient,path);
        }
        public static async Task<User> GetMeAsync()
        {
            try
            {
                // GET /me
                return await _graphClient.Me
                    .Request()
                    .Select(u => new{
                        u.DisplayName
                    })
                    .GetAsync();
            }
            catch (ServiceException ex)
            {
                Console.WriteLine($"Error getting signed-in user: {ex.Message}");
                return null;
            }
        }
    }
}