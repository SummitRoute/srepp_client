////////////////////////////////////////////////////////////////////////////
//
// Summit Route End Point Protection
//
// This source code is licensed under the BSD-style license found in the
// LICENSE file in the root directory of this source tree.
//
/////////////////////////////////////////////////////////////////////////////

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Text.RegularExpressions;

namespace srsvc
{
    public enum Decision { NO_RESPONSE = 0, ALLOW = 1, DENY = 2 }

    public static class Arbiter
    {
        /// <summary>
        /// Stips unnecessary preffixes from the path string
        /// </summary>
        /// <param name="filePath"></param>
        /// <returns></returns>
        private static string CleanPath(string filePath)
        {
            // TODO Clean up path in a cleaner way, c# is apparently really brittle
            // Can open the file then use GetFinalPathNameByHandle http://msdn.microsoft.com/en-us/library/windows/desktop/aa364962(v=vs.85).aspx
            // Also useful would be GetFileInformationByHandle 
            filePath = filePath.Trim();
            filePath = filePath.Replace("\0", "");
            filePath = filePath.Replace("\\??\\", "");

            filePath = Path.GetFullPath(filePath);
            return filePath;
        }

        private static bool ExeMatchesAttribute(RuleAttribute attr, Executable exe)
        {
            Log.Info("Checking with rule: {0},{1}", attr.AttributeType, attr.Attribute);
            if (attr.AttributeType == "path" && (new Regex(attr.Attribute)).Match(exe.Path).Success)
            {
                return true;
            }
            else if (attr.AttributeType == "md5" && Helpers.HexStringToByteArray(attr.Attribute) == exe.Md5)
            {
                return true;
            }
            else if (attr.AttributeType == "sha1" && Helpers.HexStringToByteArray(attr.Attribute) == exe.Sha1)
            {
                return true;
            }
            else if (attr.AttributeType == "sha256" && Helpers.HexStringToByteArray(attr.Attribute) == exe.Sha256)
            {
                return true;
            }
            else if (exe.Signed)
            {
                foreach (var signer in exe.Signers)
                {
                    if (attr.AttributeType == "SignerName" && attr.Attribute == signer.Name)
                    {
                        return true;
                    }
                    else if (attr.AttributeType == "Issuer" && attr.Attribute == signer.SigningCert.Issuer)
                    {
                        return true;
                    }
                    else if (attr.AttributeType == "SerialNumber" && Helpers.HexStringToByteArray(attr.Attribute) == signer.SigningCert.SerialNumber)
                    {
                        return true;
                    }
                }
            }
                

            return false;
        }

        private static Decision MakeDecisionFromRules(Executable exe)
        {
            Decision decision = Decision.ALLOW;

            var sessionFactory = Database.getSessionFactory();
            using (var session = sessionFactory.OpenSession())
            {
                var rules = session.QueryOver<Rule>()
                    .Where(e => e.Enabled == true)
                    .OrderBy(e => e.Rank).Asc
                    .List<Rule>();
                foreach (var rule in rules)
                {
                    bool match = true;
                    foreach (var attr in rule.Attrs)
                    {
                        match = match & ExeMatchesAttribute(attr, exe);
                    }
                    if (match)
                    {
                        if (rule.Allow)
                        {
                            decision = Decision.ALLOW;
                        }
                        else
                        {
                            decision = Decision.DENY;
                        }
                    }
                }
            }

            return decision;
        }

        private static Decision FinalDecisionBasedOnMode(Decision decision)
        {
            // TODO MUST set audit mode to false so we can be locked down
            bool AuditMode = true;
            if (AuditMode)
            {
                return Decision.ALLOW;
            }
            else
            {
                return decision;
            }
        }

        /// <summary>
        /// This function is called when a new process is being started.  It return the decision if the process should be allowed to run.
        /// It stores some data in the DB about this executable (such as the computed hashes).
        /// </summary>
        /// <param name="filePath"></param>
        /// <param name="ExecutableId">Database ID for the executable</param>
        /// <returns>Decision on if the process should be allowed to run</returns>
        public static Decision DecideOnProcess(string filePath, out long ExecutableId)
        {
            Log.Info("Arbiter deciding on process");
            Decision decision = Decision.ALLOW;
            List<Signer> signers = null;
            ExecutableId = 0;  // TODO Must do something where there is an error in this function so we don't record that something with ExecutableID 0 happened

            try
            {
                filePath = CleanPath(filePath);
                Log.Info("Deciding on process: {0}", filePath);

                //
                // Check if we've seen this before
                //
                DateTime lastWriteTime = File.GetLastWriteTime(filePath);
                lastWriteTime = lastWriteTime.ToUniversalTime();

                var sessionFactory = Database.getSessionFactory();
                using (var session = sessionFactory.OpenSession())
                {
                    var exes = session.QueryOver<Executable>()
                        .Where(e => e.Path == filePath)
                        .And(e =>e.LastWriteTime == lastWriteTime)
                        .List<Executable>();
                    if (exes.Count() != 0)
                    {
                        Executable foundExe = exes[0];
                        Log.Debug("Exe has been seen before");
                        ExecutableId = foundExe.Id;
                        // TODO need to use a rule engine
                        if (!foundExe.Trusted)
                        {
                            Log.Info("Deny it");
                            decision = Decision.DENY;
                        }
                        else
                        {
                            Log.Info("Allow it");
                        }
                        return decision;
                    }
                }


                //
                // If we're here then this is new, so verify it
                //
                bool isVerified = WinTrustVerify.WinTrust.Verify(filePath, out signers);
                string SignerName = "";
                if (isVerified)
                {
                    if (signers != null && signers.Count >= 1 && signers[0] != null)
                    {
                        SignerName = signers[0].Name;
                    }
                    Log.Info("File is signed by {0}", SignerName);
                }
                else
                {
                    Log.Info("File is not signed (or not trusted)");
                }

                // Compute hashes
                byte[] md5Hash, sha1Hash, sha256Hash;
                Helpers.ComputeHashes(filePath, out md5Hash, out sha1Hash, out sha256Hash);


                // Gather all this data
                var exe = new Executable
                {
                    Path = filePath,
                    LastWriteTime = lastWriteTime,
                    LastSeen = DateTime.UtcNow,
                    FirstSeen = DateTime.UtcNow,
                    LastChecked = DateTime.UtcNow,
                    Signed = (signers != null),
                    Blocked = false, // TODO Change this based on if we are in audit mode
                    Md5 = md5Hash,
                    Sha1 = sha1Hash,
                    Sha256 = sha256Hash,
                };


                //
                // Record this info to the DB
                //
                using (var session = sessionFactory.OpenSession())
                {
                    using (var transaction = session.BeginTransaction())
                    {
                        // I don't really need to check again, but I'm worried about a race
                        var exes = session.QueryOver<Executable>()
                                .Where(e => e.Path == filePath)
                                .And(e => e.LastWriteTime == lastWriteTime)
                                .List<Executable>();
                        if (exes.Count() == 0)
                        {
                            // Ensure we don't add certs to the DB if they already exist there
                            if (signers != null)
                            {
                                foreach (var signer in signers)
                                {
                                    var certs = session.QueryOver<Certificate>()
                                        .Where(e => e.SerialNumber == signer.SigningCert.SerialNumber)
                                        .And(e => e.Issuer == signer.SigningCert.Issuer)
                                        .List<Certificate>();
                                    if (certs.Count() != 0)
                                    {
                                        signer.SigningCert = certs[0];
                                        break;
                                    }
                                }

                                Database.AddSignersToExe(exe, signers);
                            }

                            //
                            // Make a decision on it 
                            //
                            decision = MakeDecisionFromRules(exe);
                            exe.Trusted = (decision == Decision.ALLOW);

                            session.Save(exe);
                            transaction.Commit();

                            ExecutableId = exe.Id;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Log.Exception(e, "Exception in DecideOnProcess");
            }

            return FinalDecisionBasedOnMode(decision);
        }
    }
}
