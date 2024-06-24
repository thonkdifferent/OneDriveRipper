//#define NOLOGIN
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
#if !NOLOGIN
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
#else
#warning Nologin mode activated. The app will NOT work. Do not distribute
            Console.WriteLine($"Welcome NOLOGIN MODE! DO NOT DISTRIBUTE!");
#endif

            string rootFolder = Environment.CurrentDirectory+$"{Path.DirectorySeparatorChar}download";
            var globalSettingsInstance = GlobalConfiguration.Instance;
            int? choice = -1;
            while (choice != 0)
            {
                Console.WriteLine();
                Console.Write($"Multithreaded downloading is "); WriteToggleState(globalSettingsInstance.DoParalelDownload);
                Console.Write($"At most ");
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write(globalSettingsInstance.MaxDownloadJobs);
                Console.ResetColor();
                Console.WriteLine(" download jobs will be used per file");
                
                Console.Write("Data verification is ");  WriteToggleState(globalSettingsInstance.VerifyDownload);
                
                Console.Write($"The files will be downloaded in ");
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine(rootFolder);
                Console.ResetColor();

                Console.Write($"If a download fails, it will be repeated at most ");
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write($"{globalSettingsInstance.MaxTryAgainFailover}");
                Console.ResetColor();
                Console.WriteLine(" times");
                
                
                Console.Write($"Download speed limit: "); WriteToggleState(globalSettingsInstance.MaximumBytesPerSecond == 0, "Unlimited",$"{globalSettingsInstance.MaximumBytesPerSecond.CalcMemoryMensurableUnit()}/s");

                
                Console.Write($"Buffer size: ");
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine(globalSettingsInstance.MaximumBufferSize.CalcMemoryMensurableUnit());
                Console.ResetColor();
                
                Console.WriteLine();
                
                Console.WriteLine("Please choose one of the following options:");
                Console.WriteLine("0. Exit");
                Console.WriteLine("1. Change download location");
                Console.WriteLine("2. Get the whole OneDrive disk");
                Console.WriteLine("3. Toggle data verification");
                Console.WriteLine("4. Change maximum number of download jobs per file");
                Console.WriteLine("5. Toggle multi-threaded downloads");
                Console.WriteLine("6. Change number of maximum retries");
                Console.WriteLine("7. Set a download speed limit");
                Console.WriteLine("8. Change the maximum buffer size (advanced users)");
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
#if !NOLOGIN
                        var task = helper.GetFilesOneDrive(rootFolder);
                        task.Wait();
#endif
                        break;
                    case 3:
                        globalSettingsInstance.VerifyDownload = !globalSettingsInstance.VerifyDownload;
                        break;
                    case 4:
                        globalSettingsInstance.MaxDownloadJobs = GetDownloadJobs() ?? globalSettingsInstance.MaxDownloadJobs;
                        break;
                    case 5:
                        globalSettingsInstance.DoParalelDownload = !globalSettingsInstance.DoParalelDownload;
                        break;
                    case 6:
                        globalSettingsInstance.MaxTryAgainFailover = GetInt("Enter the maximum tries before the download is considered failed: ") ?? globalSettingsInstance.MaxTryAgainFailover;
                        break;
                    case 7:
                        globalSettingsInstance.MaximumBytesPerSecond =
                            GetLong("Enter the download speed limit (MB/s). Type 0 for no limit: ") ?? globalSettingsInstance.MaximumBytesPerSecond/(1024*1024);
                        break;
                    case 8:
                        globalSettingsInstance.MaximumBufferSize = GetLong("Enter the maximum buffer size (MB): ",false) ??
                                                                   globalSettingsInstance.MaximumBufferSize/(1024*1024);
                        break;
                    default:
                        Console.WriteLine("Invalid choice! Please try again.");
                        break;
                }
            }
        }

        private static void WriteToggleState(bool variable, string activeMessage = "ACTIVE", string inactiveMessage = "INACTIVE")
        {
            if (variable)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine(activeMessage);
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine(inactiveMessage);
            }

            Console.ResetColor();
        }

        private static int? GetInt(string message,bool canBeNull = true)
        {
            int? answer;
            do
            {
                Console.Write(message);
                try
                {
                    var input = Console.ReadLine();
                    if (string.IsNullOrEmpty(input)) return null;
                    answer = Convert.ToInt32(input);
                    if (answer == 0 && !canBeNull)
                    {
                        Console.WriteLine("Answer cannot be 0");
                        answer = null;
                    }
                    if (answer < 0)
                    {
                        Console.WriteLine("Answer must be at least 0");
                        answer = null;
                    }
                }
                catch
                {
                    Console.WriteLine("Invalid input");
                    answer = null;
                }
            } while (answer == null);

            return answer;
        }        
        private static long? GetLong(string message,bool canBeNull = true)
        {
            
            long? answer;
            do
            {
                Console.Write(message);
                try
                {
                    var input = Console.ReadLine();
                    if (string.IsNullOrEmpty(input)) return null;
                    answer = Convert.ToInt64(input);
                    if (answer == 0 && !canBeNull)
                    {
                        Console.WriteLine("Answer cannot be 0");
                        answer = null;
                    }
                    if (answer < 0)
                    {
                        Console.WriteLine("Answer must be at least 0");
                        answer = null;
                    }
                }
                catch
                {
                    Console.WriteLine("Invalid input");
                    answer = null;
                }
            } while (answer == null);

            return answer;
        }

        private static int? GetDownloadJobs()
        {
            int? threads;
            do
            {
                Console.Write("Type in the maximum number of download jobs per file:");
                string? input = Console.ReadLine();
                if (string.IsNullOrEmpty(input)) return null;

                try
                {
                    threads = Convert.ToInt32(input);
                    if (threads <= 0)
                    {
                        throw new Exception("Must have at least one thread");
                    }

                    if (threads > Environment.ProcessorCount)
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.Write("WARNING Allowing for more jobs than logical CPUs can lead to system freezes. Are you sure you want to continue? (y/n): ");
                        Console.ResetColor();
                        var answer = Console.ReadLine() ?? "n";
                        if (!answer.StartsWith('y'))
                            return null;
                    }
                }
                catch (Exception)
                {
                    Console.WriteLine("Invalid option");
                    threads = null;
                }
            } while (threads == null);

            return threads;
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