using System;
using System.IO;
using System.Text.Json;
using OneDriveRipper.Graph;
using Azure.Identity;
using Microsoft.Kiota.Authentication.Azure;

namespace OneDriveRipper
{
    static class Program
    {
        private static bool HasWriteAccessToFolder(string folderPath)
        {
            try
            {
                FileStream fs = File.Create(folderPath + $"{Path.DirectorySeparatorChar}.test");
                fs.Close();
                File.Delete(folderPath +$"{Path.DirectorySeparatorChar}.test");
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }
        static UserSecret? LoadConfig()
        {
            UserSecretPrototype? userSecretPrototype = new UserSecretPrototype();
            UserSecret appData = new UserSecret();

            try
            {
                userSecretPrototype = JsonSerializer.Deserialize<UserSecretPrototype>(File.ReadAllText("usersecrets.json"));
            }
            catch (FileNotFoundException)
            {
                Console.WriteLine("Could not find usersecrets.json");
            }
            catch (Exception e)
            {
                Console.WriteLine($"Something went wrong\n {e.Message}\n{e.StackTrace}");
                return null;
            }

            if (string.IsNullOrEmpty(userSecretPrototype?.AppId) || string.IsNullOrEmpty(userSecretPrototype.Scopes))
            {
                return null;
            }
            appData.AppId = userSecretPrototype.AppId;
            appData.Scopes = userSecretPrototype.Scopes.Split(';');
            return appData;

        }
        static void Main()
        {
            UserSecret? data = LoadConfig();
            if (data == null)
            {
                Console.WriteLine("Missing or invalid usersecrets.json file. Make sure it is in the root directory of the program\n and try again");
                return;
            }
            Console.WriteLine("OneDrive Ripper - ThonkDifferent");


            var options = new InteractiveBrowserCredentialOptions
            {
                TenantId = "common",
                ClientId = data.AppId,
                AuthorityHost = AzureAuthorityHosts.AzurePublicCloud,
                RedirectUri = new Uri("http://localhost")
            };
            var credential = new InteractiveBrowserCredential(options);
            var authProvider =
                new AzureIdentityAuthenticationProvider(credential, ["graph.microsoft.com"], scopes: data.Scopes);
            // Initialize Graph client
            GraphHelper helper = new(authProvider);

            // Get signed in user
            var user = helper.GetMeAsync().Result;
            if (user == null)
            {
                Console.Error.WriteLine("Coult not get user data. Check your internet connection or the application permissions in your Microsoft Account dashboard");
                return;
            }
            Console.WriteLine($"Welcome {user.DisplayName}!\n");


            string rootFolder = Environment.CurrentDirectory+$"{Path.DirectorySeparatorChar}download";
            
            int? choice = -1;
            while (choice != 0)
            {
                Console.WriteLine($"The files will be downloaded in {rootFolder}");
                Console.WriteLine("Please choose one of the following options:");
                Console.WriteLine("0. Exit");
                Console.WriteLine("1. Change download location");
                Console.WriteLine("2. Get the whole OneDrive disk");
                try
                {
                    var input = Console.ReadLine();
                    if(string.IsNullOrEmpty(input)) continue;
                    choice = int.Parse(input);
                }
                catch (FormatException)
                {
                    // Set to invalid value
                    choice = -1;
                }

                switch (choice)
                {
                    case 0:
                        // Exit the program
                        Console.WriteLine("Goodbye...");
                        break;
                    case 1:
                        rootFolder = DetermineRootFolder(rootFolder);
                        break;
                    case 2:
                        if (CreateDirectoryInteractively(rootFolder) == null || !HasWriteAccessToFolder(rootFolder))
                        {
                            Console.WriteLine($"You don't have write permissions to the folder {rootFolder}. Try picking a new location");
                            break;
                        }
                        var task = helper.GetFilesOneDrive(rootFolder);
                        task.Wait();
                        break;
                    default:
                        Console.WriteLine("Invalid choice! Please try again.");
                        break;
                }
            }
        }

        private static string DetermineRootFolder(string rootFolder)
        {
            Console.WriteLine("Drag the folder or type the path you want the downloaded data to be stored");
            string tentativeDlDir = Console.ReadLine() ?? "";
            if (!Directory.Exists(tentativeDlDir))
            {
                rootFolder = CreateDirectoryInteractively(tentativeDlDir) ?? rootFolder;
                return rootFolder;
            }
            if (HasWriteAccessToFolder(tentativeDlDir))
                rootFolder = tentativeDlDir;
            
            return rootFolder;
        }

        private static string? CreateDirectoryInteractively(string tentativeDlDir)
        {

            if (Directory.Exists(tentativeDlDir)) return tentativeDlDir;
            try
            {
                Console.WriteLine(
                    $"The path {tentativeDlDir} does not exist. Would you want to create it?");
                char ans = Convert.ToChar(Console.ReadLine() ?? "");
                if (ans == 'y')
                    Directory.CreateDirectory(tentativeDlDir);
                return tentativeDlDir;
            }
            catch (UnauthorizedAccessException)
            {
                Console.WriteLine("Couldn't create the folder because you do not have write permissions to that location");
                return null;
            }
        }
    }
}