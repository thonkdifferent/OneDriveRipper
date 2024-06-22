using Microsoft.Identity.Client;
using System.Text.Json;
using System.Text.Json.Serialization;


namespace OneDriveRipper.Authentication
{
    internal class Account : IAccount
    {
        public string Username { get; set; }
        public string Environment { get; set; }
        public AccountId HomeAccountId { get; set;}

        internal Account(IAccount account)
        {
            Username = account.Username;
            Environment = account.Environment;
            HomeAccountId = account.HomeAccountId;
        }
    }
}