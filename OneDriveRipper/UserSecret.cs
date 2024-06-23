using System;

namespace OneDriveRipper
{
    public class UserSecretPrototype
    {
        public UserSecretPrototype()
        {
            AppId = "";
            Scopes = "";
        }

        public string AppId { get; init; }
        public string Scopes { get; init; }
    }

    public class UserSecret
    {
        public UserSecret()
        {
            AppId = "";
            Scopes = Array.Empty<string>();
        }
        public string AppId { get; set; }
        public string[] Scopes { get; set; }
    }
}