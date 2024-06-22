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

        public string AppId { get; set; }
        public string Scopes { get; set; }
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