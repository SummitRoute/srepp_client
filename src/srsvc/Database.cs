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

using FluentNHibernate.Mapping;
using FluentNHibernate.Cfg;
using FluentNHibernate.Cfg.Db;
using NHibernate;
using NHibernate.Cfg;
using NHibernate.Tool.hbm2ddl;

using System.Reflection;

namespace srsvc
{
    public class Database
    {
        public enum ProcessState { Exists = 0, Started, Terminated };

        private const string DB_FILE_NAME = "srepp.db";
        private static string DbFile = DB_FILE_NAME; 

        private static ISessionFactory SessionFactory = null;

        public static ISessionFactory getSessionFactory()
        {
            if (SessionFactory == null) {
                SessionFactory = CreateSessionFactory();
            }
            return SessionFactory;
        }

        private static ISessionFactory CreateSessionFactory()
        {
            // Set the database file to srepp.db in the directory where this assembly is executing from
            Uri uri = new System.Uri(Assembly.GetExecutingAssembly().CodeBase);
            DbFile = Path.Combine(Path.GetDirectoryName(Uri.UnescapeDataString(uri.AbsolutePath)), DB_FILE_NAME);
            Log.Info("Using DB file: {0}", DbFile);

            return Fluently.Configure()
                .Database(SQLiteConfiguration.Standard
                    .UsingFile(DbFile))
                .Mappings(m => m.FluentMappings.AddFromAssemblyOf<Executable>())
                .Mappings(m => m.FluentMappings.AddFromAssemblyOf<Signer>())
                .Mappings(m => m.FluentMappings.AddFromAssemblyOf<Certificate>())
                .Mappings(m => m.FluentMappings.AddFromAssemblyOf<Rule>())
                .Mappings(m => m.FluentMappings.AddFromAssemblyOf<RuleAttribute>())
                .Mappings(m => m.FluentMappings.AddFromAssemblyOf<ProcessEvent>())
                .Mappings(m => m.FluentMappings.AddFromAssemblyOf<CatalogFile>())

                .ExposeConfiguration(cfg => new SchemaUpdate(cfg).Execute(false, true))
                .BuildSessionFactory();
        }

        public static void AddSignersToExe(Executable exe, List<Signer> signers)
        {
            Log.Info("AddSignersToExe: Adding signers");
            if (signers == null)
            {
                Log.Warn("AddSignersToExe: Signers is null");
                return;
            }
            foreach (var signer in signers)
            {
                Log.Info("AddSignersToExe: Adding a signer");
                exe.AddSigner(signer);
            }
        }

        /*---------------------------------------------------------------------- */
        // Helper functions

        /// <summary>
        /// Add 
        /// </summary>
        /// <param name="rule"></param>
        public static void AddRuleToDB(Rule rule)
        {
            var sessionFactory = Database.getSessionFactory();
            using (var session = sessionFactory.OpenSession())
            {
                using (var transaction = session.BeginTransaction())
                {
                    rule.Enabled = true;
                    // Set our rule order to the next available
                    var rules = session.QueryOver<Rule>().List<Rule>();
                    rule.Rank = (uint)rules.Count;

                    session.Save(rule);
                    transaction.Commit();
                }
            }
        }

        /// <summary>
        /// Records process event info in the DB
        /// </summary>
        /// <param name="processInfo"></param>
        /// <param name="ExecutableId"></param>
        /// <param name="state"></param>
        public static void LogProcessEvent(SRSvc.PROCESS_INFO processInfo, long ExecutableId, ProcessState state)
        {
            Log.Info("Saving info for exe {0}", ExecutableId);

            var sessionFactory = Database.getSessionFactory();
            using (var session = sessionFactory.OpenSession())
            {
                using (var transaction = session.BeginTransaction())
                {
                    var processEvent = new ProcessEvent
                    {
                        ExecutableId = ExecutableId,
                        Pid = processInfo.pid,
                        Ppid = processInfo.ppid,
                        CommandLine = processInfo.CommandLine,
                        EventTime = DateTime.UtcNow,
                        State = (uint)state
                    };

                    session.Save(processEvent);
                    transaction.Commit();
                }
            }
        }

        public static void LogCatalogFile(string catalogFilePath)
        {
            Log.Info("Catalog file used: {0}", catalogFilePath);

            var sessionFactory = Database.getSessionFactory();
            using (var session = sessionFactory.OpenSession())
            {
                // Check if this catalog already exists in our DB
                var catalogs = session.QueryOver<CatalogFile>()
                        .Where(e => e.FilePath == catalogFilePath)
                        .List<CatalogFile>();
                if (catalogs.Count() == 0)
                {
                    // Does not exist, so add it
                    using (var transaction = session.BeginTransaction())
                    {
                        Log.Info("Catalog file never seen before, so adding it to the DB");

                        // TODO I should not be keeping track of the catalogs by filename instead of using the hash
                        byte[] md5Hash, sha1Hash, sha256Hash;
                        Helpers.ComputeHashes(catalogFilePath, out md5Hash, out sha1Hash, out sha256Hash);

                        var catalogFile = new CatalogFile
                        {
                            FilePath = catalogFilePath,
                            Sha256 = sha256Hash,
                            Size = (int)(new System.IO.FileInfo(catalogFilePath).Length),
                            FirstAccessTime = DateTime.UtcNow
                        };

                        Log.Info("Recorded catalog file of size {0}", catalogFile.Size);

                        session.Save(catalogFile);
                        transaction.Commit();
                    }
                }
                else
                {
                    // Already exists in the DB, so pass
                }
            }
        }
    }


    /*---------------------------------------------------------------------- */
    public class Executable
    {
        public virtual long Id { get; protected set; }

        /// <summary>
        /// Canonical path (possibly not)
        /// </summary>
        public virtual string Path { get; set; }

        /// <summary>
        ///  When a new process starts, we decide if we've seen it before based on the path and last write time.
        ///  If someone has admin they can change the last write time, but then you've already lost.
        /// </summary>
        public virtual DateTime LastWriteTime { get; set; }  


        /// <summary>
        /// Last time we saw this process started, or time it was running if it started before us.
        /// Technically, a process could run for a long time
        /// </summary>
        public virtual DateTime LastSeen { get; set; }


        /// <summary>
        /// First time this was seen
        /// </summary>
        public virtual DateTime FirstSeen { get; set; }


        /// <summary>
        /// Date we last checked if this should be trusted.
        /// If rules have been updated, we want to recheck this file.
        /// </summary>
        public virtual DateTime LastChecked { get; set; }

        /// <summary>
        /// True if it has an authenticode signature
        /// </summary>
        public virtual bool Signed { get; set; }

        /// <summary>
        /// True if the rules for this system state we trust this
        /// </summary>
        public virtual bool Trusted { get; set; }

        /// <summary>
        /// True if we blocked this when it was last attempted to run.
        /// We may not trust the file (Trusted = False), but we may be in Audit mode.
        /// </summary>
        public virtual bool Blocked { get; set; }

        public virtual byte[] Md5 { get; set; }
        public virtual byte[] Sha1 { get; set; }
        public virtual byte[] Sha256 { get; set; }


        public virtual IList<Signer> Signers { get; set; }

        public Executable()
        {
            Signers = new List<Signer>();
        }

        public virtual void AddSigner(Signer signer)
        {
            Signers.Add(signer);
        }
    }

    public class ExecutableMap : ClassMap<Executable>
    {
        public ExecutableMap()
        {
            Id(x => x.Id);
            Map(x => x.Path);
            Map(x => x.LastWriteTime);
            Map(x => x.LastSeen);
            Map(x => x.FirstSeen);
            Map(x => x.LastChecked);
            Map(x => x.Signed);
            Map(x => x.Trusted);
            Map(x => x.Blocked);
            Map(x => x.Md5);
            Map(x => x.Sha1);
            Map(x => x.Sha256);
            HasMany(x => x.Signers)
                .Cascade.All();
        }
    }



    /*---------------------------------------------------------------------- */
    public class Signer
    {
        public virtual long Id { get; protected set; }  // Internal ID
        public virtual string Name { get; set; }
        public virtual DateTime Timestamp { get; set; } // Time of signing
        public virtual Certificate SigningCert {get; set;}
    }


    public class SignerMap : ClassMap<Signer>
    {
        public SignerMap()
        {
            Id(x => x.Id);
            Map(x => x.Name);
            Map(x => x.Timestamp);
            HasOne(x => x.SigningCert).Cascade.All();
        }
    }



    /*---------------------------------------------------------------------- */
    public class Certificate
    {
        public virtual long Id { get; protected set; }  // Internal ID
        public virtual uint Version { get; set; }  // Usually 2
        public virtual string Issuer { get; set; }  // Something like "CN = COMODO Code Signing CA 2; O = COMODO CA Limited; L = Salford; S = Greater Manchester; C = GB"
        public virtual byte[] SerialNumber { get; set; }
        public virtual string DigestAlgorithm { get; set; } // sha1
        public virtual string DigestEncryptionAlgorithm { get; set; } // rsa
    }

    public class CertificateMap : ClassMap<Certificate>
    {
        public CertificateMap()
        {
            Id(x => x.Id);
            Map(x => x.Version);
            Map(x => x.Issuer);
            Map(x => x.SerialNumber);
            Map(x => x.DigestAlgorithm);
            Map(x => x.DigestEncryptionAlgorithm);
        }
    }



    /*---------------------------------------------------------------------- */
    public class Rule
    {
        public virtual long Id { get; protected set; }  // Internal ID
        public virtual uint Rank { get; set; }  // Higher numbers take precedence
        public virtual bool Enabled { get; set; }
        public virtual bool Allow { get; set; } // true = Allow; False = Deny
        public virtual DateTime LastUsed { get; set; }  // last time something flagged on this rule
        public virtual string Comment { get; set; }
        public virtual IList<RuleAttribute> Attrs { get; set; }

        public Rule()
        {
            Attrs = new List<RuleAttribute>();
        }

        public virtual void AddAttr(RuleAttribute attr)
        {
            Attrs.Add(attr);
        }
    }

    public class RuleMap : ClassMap<Rule>
    {
        public RuleMap()
        {
            Id(x => x.Id);
            Map(x => x.Rank);
            Map(x => x.Enabled);
            Map(x => x.Allow);
            Map(x => x.LastUsed);
            Map(x => x.Comment);
            HasMany(x => x.Attrs)
                .Cascade.All();
        }
    }


    /*---------------------------------------------------------------------- */
    public class RuleAttribute
    {
        public virtual long Id { get; protected set; }  // Internal ID
        public virtual string AttributeType { get; set; }
        public virtual string Attribute { get; set; }
    }

    public class RuleAttributeMap : ClassMap<RuleAttribute>
    {
        public RuleAttributeMap()
        {
            Id(x => x.Id);
            Map(x => x.AttributeType);
            Map(x => x.Attribute);
        }
    }



    /*---------------------------------------------------------------------- */
    public class ProcessEvent
    {
        public virtual long Id { get; protected set; }  // Internal ID
        public virtual long ExecutableId { get; set; }  // ID of the executable in our database
        public virtual uint Pid { get; set; }  // OS Process ID
        public virtual uint Ppid { get; set; }  // OS parent pid
        public virtual string CommandLine { get; set; }
        public virtual DateTime EventTime { get; set; }
        public virtual uint State { get; set; } // 0 = Started before us, 1 = Started, 2 = Terminated
        public virtual bool HasInformedServer { get; set; } 
    }

    public class ProcessEventMap : ClassMap<ProcessEvent>
    {
        public ProcessEventMap()
        {
            Id(x => x.Id);
            Map(x => x.ExecutableId);
            Map(x => x.Pid);
            Map(x => x.Ppid);
            Map(x => x.CommandLine);
            Map(x => x.EventTime);
            Map(x => x.State);
            Map(x => x.HasInformedServer).Default("false");
        }
    }

    /*---------------------------------------------------------------------- */
    public class CatalogFile
    {
        public virtual long Id { get; protected set; }  // Internal ID
        public virtual string FilePath { get; set; }
        public virtual byte[] Sha256 { get; set; }
        public virtual int Size { get; set; }
        public virtual DateTime FirstAccessTime { get; set; }
        public virtual bool HasInformedServer { get; set; }
    }

    public class CatalogFileMap : ClassMap<CatalogFile>
    {
        public CatalogFileMap()
        {
            Id(x => x.Id);
            Map(x => x.FilePath);
            Map(x => x.Sha256);
            Map(x => x.Size);
            Map(x => x.FirstAccessTime);
            Map(x => x.HasInformedServer).Default("false");
        }
    }
}
