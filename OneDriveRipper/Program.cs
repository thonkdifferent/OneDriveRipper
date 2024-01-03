using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using OneDriveRipper.Authentication;
using OneDriveRipper.Graph;

namespace OneDriveRipper
{
    class Program
    {
        private static bool HasWriteAccessToFolder(string folderPath)
        {
            try
            {
                FileStream fs = File.Create(folderPath + "/.test");
                fs.Close();
                File.Delete(folderPath + "/.test");
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }
        static UserSecret LoadConfig()
        {
            UserSecretPrototype userSecretPrototype = new UserSecretPrototype();
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

            if (string.IsNullOrEmpty(userSecretPrototype.AppId) || string.IsNullOrEmpty(userSecretPrototype.Scopes))
            {
                Console.WriteLine(userSecretPrototype.AppId);
                Console.WriteLine(userSecretPrototype.Scopes);
                return null;
            }
            appData.AppId = userSecretPrototype.AppId;
            appData.Scopes = userSecretPrototype.Scopes.Split(';');
            return appData;

        }
        static async Task Main()
        {
            UserSecret data = LoadConfig();
            if (data == null)
            {
                Console.WriteLine("Missing or invalid usersecrets.json file. Make sure it is in the root directory of the program\n and try again");
                return;
            }
            Console.WriteLine("OneDrive Ripper - ThonkDifferent");
            var authProvider = new DeviceCodeAuthProvider(data.AppId, data.Scopes);
            var accessToken = authProvider.GetAccessToken().Result;
            if (string.IsNullOrEmpty(accessToken))
            {
                Console.WriteLine("Couldn't authenticate. Halting");
                return;
            }
            // Initialize Graph client
            GraphHelper.Initialize(authProvider);

            // Get signed in user
            var user = GraphHelper.GetMeAsync().Result;
            Console.WriteLine($"Welcome {user.DisplayName}!\n");


            string rootFolder = Environment.CurrentDirectory+"/download";
            
            int choice = -1;
            while (choice != 0)
            {
                Console.WriteLine($"The files will be downloaded in {rootFolder}");
                Console.WriteLine("Please choose one of the following options:");
                Console.WriteLine("0. Exit");
                Console.WriteLine("1. Display access token");
                Console.WriteLine("2. Change download location");
                Console.WriteLine("3. Get the whole OneDrive disk");
                try
                {
                    choice = int.Parse(Console.ReadLine());
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
                        Console.WriteLine($"Access token: {accessToken}\n");
                        break;
                    case 2:
                        Console.WriteLine("Drag the folder or type the path you want the downloaded data to be stored");
                        string tentativeDlDir = Console.ReadLine();
                        if (!Directory.Exists(tentativeDlDir))
                        {
                            bool ok = true;
                            try
                            {
                                Console.WriteLine(
                                    $"The path {tentativeDlDir} does not exist. Would you want to create it?");
                                char ans = Convert.ToChar(Console.ReadLine());
                                if (ans == 'y')
                                    Directory.CreateDirectory(tentativeDlDir);
                            }
                            catch (UnauthorizedAccessException)
                            {
                                Console.WriteLine("Couldn't create the folder because you do not have write permissions to that location");
                                ok = false;
                            }

                            if (ok)
                            {
                                rootFolder = tentativeDlDir;
                            }
                        }
                        else
                        {
                            if (HasWriteAccessToFolder(tentativeDlDir))
                                rootFolder = tentativeDlDir;
                        }
                        break;
                    case 3:
                        if (!Directory.Exists(rootFolder))
                        {
                            Directory.CreateDirectory(rootFolder);
                        }
                        if (HasWriteAccessToFolder(rootFolder))
                        {
                            await GraphHelper.GetFilesOneDrive(rootFolder);
                        }
                        else
                        {
                            Console.WriteLine($"You don't have write permissions to the folder {rootFolder}. Try picking a new location");
                        }
                        break;
                    default:
                        Console.WriteLine("Invalid choice! Please try again.");
                        break;
                }
            }
        }
    }
}