using Microsoft.Identity.Client;
using System.Text.Json;
using System.Text.Json.Serialization;


namespace OneDriveRipper.Authentication
{
    internal class Account : IAccount
    {
        public required string Username { get; set; }
        public required string Environment { get; set; }
        public required AccountId HomeAccountId { get; set;}
    }
}