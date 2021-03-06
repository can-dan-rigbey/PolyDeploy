﻿using Cantarus.Libraries.Encryption;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Web.Script.Serialization;

namespace DeployClient
{
    class Program
    {
        private static bool IsSilent = false;
        private static bool NoPrompt = false;

        enum ExitCode : int
        {
            Success = 0,
            Error = 1,
            NoModulesFound = 2,
            UserExit = 3,
            InstallFailure = 4
        }

        static void Main(string[] args)
        {
            try
            {
                foreach (string arg in args)
                {
                    if (arg.ToLower().Contains("--silent"))
                    {
                        IsSilent = true;
                    }

                    if (arg.ToLower().Contains("--no-prompt"))
                    {
                        NoPrompt = true;
                    }
                }

                // Output start.
                WriteLine("*** PolyDeploy Client ***");
                WriteLine();

                // Get the properties we need.
                string targetUri = Properties.Settings.Default.TargetUri;
                string apiKey = Properties.Settings.Default.APIKey;
                string encryptionKey = Properties.Settings.Default.EncryptionKey;

                // Do we have a target uri.
                if (targetUri.Equals(string.Empty))
                {
                    throw new Exception("No target uri has been set.");
                }

                // Do we have an api key?
                if (apiKey.Equals(string.Empty))
                {
                    throw new Exception("No api key has been set.");
                }

                // Do we have an encryption key?
                if (encryptionKey.Equals(string.Empty))
                {
                    throw new Exception("No encryption key has been set.");
                }

                // Output identifying module archives.
                WriteLine("Identifying module archives...");

                // Read zip files in current directory.
                string currentDirectory = Directory.GetCurrentDirectory();
                List<string> zipFiles = new List<string>(Directory.GetFiles(currentDirectory, "*.zip"));

                // Is there something to do?
                if (zipFiles.Count <= 0)
                {
                    // No, exit.
                    WriteLine("No module archives found.");
                    WriteLine("Exiting.");
                    ReadLine();
                    Environment.Exit((int)ExitCode.NoModulesFound);
                }

                // Inform user of modules found.
                WriteLine(string.Format("Found {0} module archives in {1}:", zipFiles.Count, currentDirectory));

                foreach (string zipFile in zipFiles)
                {
                    WriteLine(string.Format("\t{0}. {1}", zipFiles.IndexOf(zipFile) + 1, Path.GetFileName(zipFile)));
                }
                WriteLine();

                if (!NoPrompt)
                {
                    // Prompt to continue.
                    WriteLine("Would you like to continue? (y/n)");

                    // Continue?
                    if (!Confirm())
                    {
                        // No, exit.
                        WriteLine("Exiting.");
                        Environment.Exit((int)ExitCode.UserExit);
                    }
                    WriteLine();
                }

                // Inform user of encryption.
                WriteLine("Starting encryption and upload...");

                // Get a session.
                string sessionGuid = API.CreateSession();

                WriteLine(string.Format("Got session: {0}", sessionGuid));

                DateTime startTime = DateTime.Now;

                foreach (string zipFile in zipFiles)
                {

                    using (FileStream fs = new FileStream(zipFile, FileMode.Open))
                    {
                        WriteLine(string.Format("\t{0}", Path.GetFileName(zipFile)));
                        Write("\t\t...encrypting...");

                        using (Stream es = Crypto.Encrypt(fs, Properties.Settings.Default.EncryptionKey))
                        {
                            Write("uploading...");

                            API.AddPackageAsync(sessionGuid, es, Path.GetFileName(zipFile));
                        }

                        WriteLine("done.");
                    }
                }

                WriteLine(string.Format("Finished encryption and upload in {0} ms.", (DateTime.Now - startTime).TotalMilliseconds));
                WriteLine();

                WriteLine("Starting installation...");

                DateTime installStartTime = DateTime.Now;
                JavaScriptSerializer jsonSer = new JavaScriptSerializer();

                // Start.
                SortedList<string, dynamic> results = null;

                if (!API.Install(sessionGuid, out results))
                {
                    DateTime abortTime = DateTime.Now.AddMinutes(10);
                    TimeSpan interval = new TimeSpan(0, 0, 0, 2);

                    int status = -1;
                    string previousPrint = null;

                    // While the process isn't complete and we haven't exceeded our abort time.
                    while (status < 2 && DateTime.Now < abortTime)
                    {
                        // Get response.
                        Dictionary<string, dynamic> response = API.GetSession(sessionGuid);

                        // Is there a status key?
                        if (response.ContainsKey("Status"))
                        {   
                            // Yes, get the status.
                            status = response["Status"];
                        }

                        // Is there a response key?
                        if (response.ContainsKey("Response"))
                        {
                            // Yes, get the response.
                            results = jsonSer.Deserialize<SortedList<string, dynamic>>(response["Response"]);
                        }

                        // As long as we have something.
                        if (status != -1 && results != null)
                        {
                            // Build feedback.
                            string print = BuildUpdateString(results);
                            
                            // Same as previous feedback?
                            if (print != previousPrint)
                            {
                                WriteLine(print);
                                previousPrint = print;
                            }
                        }

                        // Is finished?
                        if (status == 2)
                        {
                            break;
                        }

                        // Sleep.
                        System.Threading.Thread.Sleep(interval);
                    }
                }
                else
                {
                    // Build feedback.
                    string print = BuildUpdateString(results);

                    // Print feedback.
                    WriteLine(print);   
                }

                // Finished install.
                WriteLine(string.Format("Finished installation in {0} ms.", (DateTime.Now - installStartTime).TotalMilliseconds));
                ReadLine();
            }
            catch (Exception ex)
            {
                // Output exception message and stack trace.
                WriteLine(string.Format("Exception caught at: {0}.", DateTime.Now.ToString()));
                WriteException(ex);

                ReadLine();
                Environment.Exit((int)ExitCode.Error);
            }
        }

        private static string BuildUpdateString(SortedList<string, dynamic> results)
        {
            // Get counts.
            int attempted = 0;
            int succeeded = 0;
            int failed = 0;

            foreach (KeyValuePair<string, dynamic> kvp in results)
            {
                Dictionary<string, dynamic> module = kvp.Value;

                if (module.ContainsKey("Attempted") && (bool)module["Attempted"])
                {
                    attempted++;

                    if (module.ContainsKey("Success") && (bool)module["Success"])
                    {
                        succeeded++;
                    }
                    else
                    {
                        failed++;
                    }
                }
            }

            return string.Format("\t{0}/{1} module archives processed, {2}/{0} succeeded.", attempted, results.Count, succeeded);
        }

        private static void WriteException(Exception ex, int maxDepth = 10, int depth = 0)
        {
            WriteLine(ex.Message);
            WriteLine(ex.StackTrace);

            if (depth < maxDepth && ex.InnerException != null)
            {
                depth++;
                WriteException(ex.InnerException, maxDepth, depth);
            }
        }

        private static void Write(string message)
        {
            if(IsSilent)
            {
                return;
            }

            Console.Write(message);
        }

        private static void WriteLine(string message = "")
        {
            if(IsSilent)
            {
                return;
            }

            Console.WriteLine(message);
        }

        private static string ReadLine()
        {
            if (IsSilent || NoPrompt)
            {
                return null;
            }

            return Console.ReadLine();
        }

        private static bool Confirm()
        {
            if (IsSilent || NoPrompt)
            {
                return true;
            }

            char ch = Console.ReadKey(true).KeyChar;

            if (ch.Equals('y') || ch.Equals('Y'))
            {
                return true;
            }

            return false;
        }
    }
}
