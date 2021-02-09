using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Linq;
using SteamKit2;
using System.Collections.Generic;

namespace DepotDumper
{
    class Program
    {
        public static StreamWriter sw;
        public static StreamWriter sw2;
        private static Steam3Session steam3;

        static void Main(string[] args)
        {

            Console.Write("Username: ");
            string user = Console.ReadLine();
            string password;

            Console.Write("Password: ");
            if (Console.IsInputRedirected)
            {
                password = Console.ReadLine();
            }
            else
            {
                // Avoid console echoing of password
                password = Util.ReadPassword();
                Console.WriteLine();
            }

            sw = new StreamWriter($"{user}_steam.keys");
            sw.AutoFlush = true;
            sw2 = new StreamWriter($"{user}_steam.appids");
            sw2.AutoFlush = true;

            Config.SuppliedPassword = password;
            AccountSettingsStore.LoadFromFile("xxx");

            steam3 = new Steam3Session(
               new SteamUser.LogOnDetails()
               {
                   Username = user,
                   Password = password,
                   ShouldRememberPassword = false,
                   LoginID = 0x534B32, // "SK2"
               }
            );

            var steam3Credentials = steam3.WaitForCredentials();

            if (!steam3Credentials.IsValid)
            {
                Console.WriteLine("Unable to get steam3 credentials.");
                return;
            }

            Console.WriteLine("Getting licenses...");
            steam3.WaitUntilCallback(() => { }, () => { return steam3.Licenses != null; });

            IEnumerable<uint> licenseQuery;
            licenseQuery = steam3.Licenses.Select(x => x.PackageID).Distinct();
            steam3.RequestPackageInfo(licenseQuery);

            foreach (var license in licenseQuery)
            {
                SteamApps.PICSProductInfoCallback.PICSProductInfo package;
                if (steam3.PackageInfo.TryGetValue(license, out package) && package != null)
                {
                    foreach (uint appId in package.KeyValues["appids"].Children.Select(x => x.AsUnsignedInteger()))
                    {
                        steam3.RequestAppInfo(appId);

                        SteamApps.PICSProductInfoCallback.PICSProductInfo app;
                        if (!steam3.AppInfo.TryGetValue(appId, out app) || app == null)
                        {
                            continue;
                        }

                        KeyValue appinfo = app.KeyValues;
                        KeyValue depots = appinfo.Children.Where(c => c.Name == "depots").FirstOrDefault();
                        KeyValue common = appinfo.Children.Where(c => c.Name == "common").FirstOrDefault();
                        KeyValue config = appinfo.Children.Where(c => c.Name == "config").FirstOrDefault();


                        if (depots == null)
                        {
                            continue;
                        }

                        string appName = "** UNKNOWN **";
                        if( common != null)
                        {
                            KeyValue nameKV = common.Children.Where(c => c.Name == "name").FirstOrDefault();
                            if(nameKV != null)
                            {
                                appName = nameKV.AsString();
                            }
                        }

                        Console.WriteLine("Got AppInfo for {0}: {1}", appId, appName);

                        sw2.WriteLine($"{appId};{appName}");

                        foreach (var depotSection in depots.Children)
                        {
                            uint id = uint.MaxValue;

                            if (!uint.TryParse(depotSection.Name, out id) || id == uint.MaxValue)
                                continue;

                            if (depotSection.Children.Count == 0)
                                continue;

                            if (config == KeyValue.Invalid)
                                continue;

                            if (!AccountHasAccess(id))
                                continue;

                            int attempt = 1;
                            while (!steam3.DepotKeys.ContainsKey(id) && attempt <= 3)
                            {
                                if (attempt > 1)
                                {
                                    Console.WriteLine($"Retrying... ({attempt})");
                                }
                                steam3.RequestDepotKey(id, appId);
                                attempt++;
                            }

                        }
                    }
                }
            }

            sw.Close();
            sw2.Close();

            Console.WriteLine("\nDone!!");

        }

        static bool AccountHasAccess( uint depotId )
        {
            IEnumerable<uint> licenseQuery = steam3.Licenses.Select(x => x.PackageID).Distinct();
            steam3.RequestPackageInfo(licenseQuery);

            foreach (var license in licenseQuery)
            {
                SteamApps.PICSProductInfoCallback.PICSProductInfo package;
                if (steam3.PackageInfo.TryGetValue(license, out package) && package != null)
                {
                    if (package.KeyValues["appids"].Children.Any(child => child.AsUnsignedInteger() == depotId))
                        return true;

                    if (package.KeyValues["depotids"].Children.Any(child => child.AsUnsignedInteger() == depotId))
                        return true;
                }
            }

            return false;
        }

    }

}
