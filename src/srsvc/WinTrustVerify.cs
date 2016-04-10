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
using System.Text;
using System.IO;

using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Microsoft.Win32;

using System.Security.Cryptography.X509Certificates;

namespace srsvc
{
    // Multiple sigs: https://stackoverflow.com/questions/24892531/reading-multiple-signatures-from-executable-file

    // Much of this code is based on: http://processhacker.sourceforge.net/doc/verify_8c_source.html
    // Should also consider just using CryptQueryObject as discussed:
    //   - https://stackoverflow.com/questions/7241453/read-and-validate-certificate-from-executable
    //   - https://support.microsoft.com/default.aspx?scid=kb;en-us;323809

    // Can check structs from here: http://blogs.msdn.com/b/alejacma/archive/2007/11/23/p-invoking-cryptoapi-in-net-c-version.aspx

    /// <summary>
    /// Interop to wintrust.dll for verifying the authenticode signature in PE files
    /// </summary>
    public class WinTrustVerify
    {
        #region WinTrustData struct field enums
        enum WinTrustDataUIChoice : uint
        {
            All = 1,
            None = 2,
            NoBad = 3,
            NoGood = 4
        }

        enum WinTrustDataRevocationChecks : uint
        {
            None = 0x00000000,
            WholeChain = 0x00000001
        }

        enum WinTrustDataChoice : uint
        {
            File = 1,
            Catalog = 2,
            Blob = 3,
            Signer = 4,
            Certificate = 5
        }

        public enum WinTrustDataStateAction : uint
        {
            Ignore = 0x00000000,
            Verify = 0x00000001,
            Close = 0x00000002,
            AutoCache = 0x00000003,
            AutoCacheFlush = 0x00000004
        }

        [FlagsAttribute]
        enum WinTrustDataProvFlags : uint
        {
            UseIe4TrustFlag = 0x00000001,
            NoIe4ChainFlag = 0x00000002,
            NoPolicyUsageFlag = 0x00000004,
            RevocationCheckNone = 0x00000010,
            RevocationCheckEndCert = 0x00000020,
            RevocationCheckChain = 0x00000040,
            RevocationCheckChainExcludeRoot = 0x00000080,
            SaferFlag = 0x00000100,        // Used by software restriction policies. Should not be used.
            HashOnlyFlag = 0x00000200,
            UseDefaultOsverCheck = 0x00000400,
            LifetimeSigningFlag = 0x00000800,
            CacheOnlyUrlRetrieval = 0x00001000,      // affects CRL retrieval and AIA retrieval
            DisableMD2andMD4 = 0x00002000      // Win7 SP1+: Disallows use of MD2 or MD4 in the chain except for the root 
        }

        enum WinTrustDataUIContext : uint
        {
            Execute = 0,
            Install = 1
        }
        #endregion

        #region WinTrust structures

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        class CryptProviderData // CRYPT_PROVIDER_DATA struct
        {
            public UInt32 cbStruct;         // DWORD cbStruct;
            IntPtr pWintrustData;           // WINTRUST_DATA *pWintrustData;
            Boolean fOpenedFile;            // BOOL fOpenedFile;
            IntPtr hWndParent;              // HWND hWndParent; 
            IntPtr pgActionID;              // GUID* pgActionID;
            IntPtr hProv;                   // HCRYPTPROV hProv;
            UInt32 dwError;                 // DWORD dwError;
            UInt32 dwRegSecuritySettings;   // DWORD dwRegSecuritySettings;
            UInt32 dwRegPolicySettings;     // DWORD dwRegPolicySettings;
            IntPtr psPfns;                  // CRYPT_PROVIDER_FUNCTIONS* psPfns;
            UInt32 cdwTrustStepErrors;      // DWORD cdwTrustStepErrors;
            IntPtr padwTrustStepErrors;     // DWORD* padwTrustStepErrors;
            UInt32 chStores;                // DWORD chStores;
            IntPtr pahStores;               // HCERTSTORE* pahStores;
            UInt32 dwEncoding;              // DWORD dwEncoding;
            IntPtr hMsg;                    // HCRYPTMSG hMsg;
            UInt32 csSigners;               // DWORD csSigners;
            IntPtr pasSigners;              // CRYPT_PROVIDER_SGNR* pasSigners;
            UInt32 csProvPrivData;          // DWORD csProvPrivData;
            IntPtr pasProvPrivData;         // CRYPT_PROVIDER_PRIVDATA pasProvPrivData;
            UInt32 dwSubjectChoice;         // DWORD dwSubjectChoice;
            IntPtr pPDSip;                  // union { _PROVDATA_SIP *pPDSip; };
            IntPtr pszUsageOID;             // char *pszUsageOID;
            Boolean fRecallWithState;       // BOOL fRecallWithState;
            UInt64 sftSystemTime;           // FILETIME sftSystemTime;
            IntPtr pszCTLSignerUsageOID;    // char *pszCTLSignerUsageOID;
            UInt32 dwProvFlags;             // DWORD dwProvFlags;
            UInt32 dwFinalError;            // DWORD dwFinalError;
            IntPtr pRequestUsage;           // PCERT_USAGE_MATCH pRequestUsage;
            UInt32 dwTrustPubSettings;      // DWORD dwTrustPubSettings;
            UInt32 dwUIStateFlags;          // DWORD dwUIStateFlags;
        }



        [StructLayout(LayoutKind.Sequential, Pack = 1, CharSet = CharSet.Unicode)]
        public class CryptProviderSgnr // CRYPT_PROVIDER_SGNR struct
        {
            public UInt32 cbStruct;                // DWORD cbStruct;
            public UInt64 sftVerifyAsOf;           // FILETIME sftVerifyAsOf;
            public UInt32 csCertChain;      // DWORD csCertChain;
            public IntPtr pasCertChain;     // CRYPT_PROVIDER_CERT* pasCertChain;
            public UInt32 dwSignerType;            // DWORD dwSignerType;
            public IntPtr psSigner;         // CMSG_SIGNER_INFO* psSigner;
            public UInt32 dwError;                 // DWORD dwError;
            public UInt32 csCounterSigners;        // DWORD csCounterSigners;
            public IntPtr pasCounterSigners;       // CRYPT_PROVIDER_SGNR* pasCounterSigners;
            public IntPtr pChainContext;           // PCCERT_CHAIN_CONTEXT pChainContext;
        }


        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public class CryptProviderCert // CRYPT_PROVIDER_CERT struct
        {
            UInt32 cbStruct;            // DWORD cbStruct;
            public IntPtr pCert;        // PCCERT_CONTEXT pCert;
            Boolean fCommercial;        // BOOL fCommercial;
            Boolean fTrustedRoot;       // BOOL fTrustedRoot;
            Boolean fSelfSigned;        // BOOL fSelfSigned;
            Boolean fTestCert;          // BOOL fTestCert;
            UInt32 dwRevokedReason;     // DWORD dwRevokedReason;
            UInt32 dwConfidence;        // DWORD dwConfidence;
            UInt32 dwError;             // DWORD dwError;
            IntPtr pTrustListConte;     // CTL_CONTEXT* pTrustListContext;
            Boolean fTrustListSignerCert; // BOOL fTrustListSignerCert;
            IntPtr pCtlContext;         // PCCTL_CONTEXT pCtlContext;
            UInt32 dwCtlError;          // DWORD dwCtlError;
            Boolean fIsCyclic;          // BOOL fIsCyclic;
            IntPtr pChainElement;       // PCERT_CHAIN_ELEMENT pChainElement;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public class CertContext // CERT_CONTEXT struct
        {
            UInt32 dwCertEncodingType;  // DWORD dwCertEncodingType;
            IntPtr pbCertEncoded;       // BYTE *pbCertEncoded;
            UInt32 cbCertEncoded;       // DWORD cbCertEncoded;
            public IntPtr pCertInfo;    // PCERT_INFO pCertInfo;
            IntPtr hCertStore;          // HCERTSTORE hCertStore;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1, CharSet = CharSet.Unicode)]
        public class CertInfo // CERT_INFO struct
        {
            public UInt32 dwVersion;               // DWORD dwVersion;
            public CRYPTOAPI_BLOB SerialNumber;    // CRYPT_INTEGER_BLOB SerialNumber;
            public CryptAlgorithmId SignatureAlgorithm;    // CRYPT_ALGORITHM_IDENTIFIER SignatureAlgorithm;
            public CRYPTOAPI_BLOB Issuer;   // CERT_NAME_BLOB Issuer;
            UInt64 NotBefore;               // FILETIME NotBefore;
            UInt64 NotAfter;                // FILETIME NotAfter;
            public CRYPTOAPI_BLOB Subject;  // CERT_NAME_BLOB Subject;
            public CertPublicKeyInfo SubjectPublicKeyInfoAlgo; // CERT_PUBLIC_KEY_INFO SubjectPublicKeyInfo;
            CRYPT_BIT_BLOB IssuerUniqueId;  // CRYPT_BIT_BLOB IssuerUniqueId;
            CRYPT_BIT_BLOB SubjectUniqueId; // CRYPT_BIT_BLOB SubjectUniqueId;
            UInt32 cExtension;              // DWORD cExtension;
            IntPtr rgExtension;             // PCERT_EXTENSION rgExtension;
        }


        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public class CRYPTOAPI_BLOB // CRYPT_INTEGER_BLOB, CERT_NAME_BLOB, CRYPT_OBJID_BLOB struct
        {
            public Int32 cbData;
            public IntPtr pbData;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct CRYPT_BIT_BLOB
        {
            public Int32 cbData;
            public IntPtr pbData;
            public Int32 cUnusedBits;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public class CryptAlgorithmId // CRYPT_ALGORITHM_IDENTIFIER struct
        {
            [MarshalAs(UnmanagedType.LPStr)]
            public String pszObjId;         // LPSTR pszObjId;
            CRYPTOAPI_BLOB Parameters;      // CRYPT_OBJID_BLOB Parameters;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public class CertPublicKeyInfo // CERT_PUBLIC_KEY_INFO struct
        {
            public CryptAlgorithmId Algorithm;   // CRYPT_ALGORITHM_IDENTIFIER Algorithm;
            CRYPT_BIT_BLOB PublicKey;            // CRYPT_BIT_BLOB PublicKey;
        }


        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        class CatalogInfo // CATALOG_INFO struct
        {
            UInt32 cbStruct;                // DWORD cbStruct;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string wszCatalogFile;   // WCHAR wszCatalogFile[MAX_PATH];
        }


        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        class WinTrustFileInfo
        {
            UInt32 StructSize = (UInt32)Marshal.SizeOf(typeof(WinTrustFileInfo));
            IntPtr pszFilePath;                     // required, file name to be verified
            IntPtr hFile = IntPtr.Zero;             // optional, open handle to FilePath
            IntPtr pgKnownSubject = IntPtr.Zero;    // optional, subject type if it is known

            public WinTrustFileInfo(String _filePath)
            {
                pszFilePath = Marshal.StringToCoTaskMemAuto(_filePath);
            }
            ~WinTrustFileInfo()
            {
                Marshal.FreeCoTaskMem(pszFilePath);
            }
        }


        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        class WinTrustCatalogInfo // WINTRUST_CATALOG_INFO 
        {
            UInt32 StructSize = (UInt32)Marshal.SizeOf(typeof(WinTrustCatalogInfo));
            UInt32 dwCatalogVersion = 0;
            IntPtr pcwszCatalogFilePath;                // Path to the catalog
            IntPtr pcwszMemberTag;                      // The calculated hash
            IntPtr pcwszMemberFilePath;                 // The file (not catalog)
            IntPtr hMemberFile = IntPtr.Zero;           // optional
            IntPtr pbCalculatedFileHash = IntPtr.Zero;  // optional
            UInt32 cbCalculatedFileHash = 0;            // optional
            IntPtr pcCatalogContext = IntPtr.Zero;
            IntPtr hCatAdmin;

            public WinTrustCatalogInfo(String _catalogFilePath, String _memberTag, String _memberFilePath, IntPtr _hCatAdmin)
            {
                pcwszCatalogFilePath = Marshal.StringToCoTaskMemAuto(_catalogFilePath);
                pcwszMemberTag = Marshal.StringToCoTaskMemAuto(_memberTag);
                pcwszMemberFilePath = Marshal.StringToCoTaskMemAuto(_memberFilePath);
                hCatAdmin = _hCatAdmin;
            }
            ~WinTrustCatalogInfo()
            {
                Marshal.FreeCoTaskMem(pcwszCatalogFilePath);
                Marshal.FreeCoTaskMem(pcwszMemberTag);
                Marshal.FreeCoTaskMem(pcwszMemberFilePath);
            }
        }


        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        class WinTrustData  // WINTRUST_DATA struct
        {
            UInt32 cbStruct = (UInt32)Marshal.SizeOf(typeof(WinTrustData));
            IntPtr pPolicyCallbackData = IntPtr.Zero;
            IntPtr pSIPClientData = IntPtr.Zero;
            // required: UI choice
            WinTrustDataUIChoice dwUIChoice = WinTrustDataUIChoice.None;
            // required: certificate revocation check options
            WinTrustDataRevocationChecks fdwRevocationChecks = WinTrustDataRevocationChecks.None;
            // required: which structure is being passed in?
            WinTrustDataChoice dwUnionChoice = WinTrustDataChoice.File;
            // individual file
            IntPtr FileInfoPtr;
            public WinTrustDataStateAction dwStateAction = WinTrustDataStateAction.Ignore;
            public IntPtr hWVTStateData = IntPtr.Zero;  // HANDLE hWVTStateData;
            String URLReference = null;
            WinTrustDataProvFlags ProvFlags = WinTrustDataProvFlags.RevocationCheckChainExcludeRoot;
            WinTrustDataUIContext UIContext = WinTrustDataUIContext.Execute;

            // constructor for silent WinTrustDataChoice.File check
            public WinTrustData(String _fileName, bool isCatalog, String _hash, String _catalogPath, IntPtr hCatAdmin)
            {
                // On Win7SP1+, don't allow MD2 or MD4 signatures
                if ((Environment.OSVersion.Version.Major > 6) ||
                    ((Environment.OSVersion.Version.Major == 6) && (Environment.OSVersion.Version.Minor > 1)) ||
                    ((Environment.OSVersion.Version.Major == 6) && (Environment.OSVersion.Version.Minor == 1) && !String.IsNullOrEmpty(Environment.OSVersion.ServicePack)))
                {
                    ProvFlags |= WinTrustDataProvFlags.DisableMD2andMD4;
                }

                if (isCatalog)
                {
                    dwUnionChoice = WinTrustDataChoice.Catalog;
                    WinTrustCatalogInfo wtfiData = new WinTrustCatalogInfo(_catalogPath, _hash, _fileName, hCatAdmin);

                    FileInfoPtr = Marshal.AllocCoTaskMem(Marshal.SizeOf(typeof(WinTrustCatalogInfo)));
                    Marshal.StructureToPtr(wtfiData, FileInfoPtr, false);
                }
                else
                {
                    WinTrustFileInfo wtfiData = new WinTrustFileInfo(_fileName);

                    FileInfoPtr = Marshal.AllocCoTaskMem(Marshal.SizeOf(typeof(WinTrustFileInfo)));
                    Marshal.StructureToPtr(wtfiData, FileInfoPtr, false);
                }
            }
            ~WinTrustData()
            {
                Marshal.FreeCoTaskMem(FileInfoPtr);
            }
        }
        #endregion

        public enum WinVerifyTrustResult : uint
        {
            Success = 0,
            ProviderUnknown = 0x800b0001,           // Trust provider is not recognized on this system
            ActionUnknown = 0x800b0002,             // Trust provider does not support the specified action
            SubjectFormUnknown = 0x800b0003,        // Trust provider does not support the form specified for the subject
            SubjectNotTrusted = 0x800b0004,         // Subject failed the specified verification action
            FileNotSigned = 0x800B0100,             // TRUST_E_NOSIGNATURE - File was not signed
            SubjectExplicitlyDistrusted = 0x800B0111,   // Signer's certificate is in the Untrusted Publishers store
            SignatureOrFileCorrupt = 0x80096010,    // TRUST_E_BAD_DIGEST - file was probably corrupt
            SubjectCertExpired = 0x800B0101,        // CERT_E_EXPIRED - Signer's certificate was expired
            SubjectCertificateRevoked = 0x800B010   // CERT_E_REVOKED Subject's certificate was revoked
        }

        public sealed class WinTrust
        {
            #region Native imports
            [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = true, CharSet = CharSet.Unicode)]
            public static extern Boolean CryptCATAdminCalcHashFromFileHandle(
                SafeFileHandle hFile,   // _In_     HANDLE hFile,
                ref UInt32 pcbHash,     // _Inout_  DWORD *pcbHash,
                IntPtr pbHash,          // _In_     BYTE *pbHash,
                UInt32 dwFlags          // _In_     DWORD dwFlags
                );

            [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = true, CharSet = CharSet.Unicode)]
            public static extern Boolean CryptCATAdminCalcHashFromFileHandle2(
                IntPtr hCatAdmin,       // _In_       HCATADMIN hCatAdmin,
                SafeFileHandle hFile,   // _In_       HANDLE hFile,
                ref UInt32 pcbHash,     // _Inout_     DWORD *pcbHash,
                IntPtr pbHash,          // _Out_writes_bytes_to_opt_(*pcbHash,*pcbHash)BYTE *pbHash,
                UInt32 dwFlags          // _Reserved_  DWORD dwFlags
            );

            [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = true, CharSet = CharSet.Unicode)]
            public static extern Boolean CryptCATAdminAcquireContext(
                [Out] out IntPtr phCatAdmin,    // _Out_  HCATADMIN *phCatAdmin,
                IntPtr pgSubsystem,             // _In_   const GUID *pgSubsystem,
                UInt32 dwFlags                  // _In_   DWORD dwFlags
            );

            [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = true, CharSet = CharSet.Unicode)]
            public static extern Boolean CryptCATAdminAcquireContext2(
                [Out] out IntPtr phCatAdmin,    // _Out_       HCATADMIN *phCatAdmin,
                IntPtr pgSubsystem,             // _In_opt_    const GUID *pgSubsystem,
                [MarshalAs(UnmanagedType.LPWStr)] string pwszHashAlgorithm,   // _In_opt_    PCWSTR pwszHashAlgorithm,
                IntPtr pStrongHashPolicy,       // _In_opt_    PCCERT_STRONG_SIGN_PARA pStrongHashPolicy,
                UInt32 dwFlags                 // _Reserved_  DWORD dwFlags
            );

            [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = true, CharSet = CharSet.Unicode)]
            public static extern IntPtr CryptCATAdminEnumCatalogFromHash(
                IntPtr hCatAdmin,           // _In_  HCATADMIN hCatAdmin,
                IntPtr pbHash,              // _In_  BYTE *pbHash,
                UInt32 cbHash,              // _In_  DWORD cbHash,
                UInt32 dwFlags,             // _In_  DWORD dwFlags,
                ref IntPtr phPrevCatInfo        // _In_  HCATINFO *phPrevCatInfo
            );

            [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = true, CharSet = CharSet.Unicode)]
            public static extern Boolean CryptCATCatalogInfoFromContext(
                IntPtr hCatInfo,            // _In_     HCATINFO hCatInfo,
                IntPtr psCatInfo,           // _Inout_  CATALOG_INFO *psCatInfo,
                UInt32 dwFlags              // _In_     DWORD dwFlags
            );

            [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = true, CharSet = CharSet.Unicode)]
            public static extern Boolean CryptCATAdminReleaseCatalogContext(
                IntPtr hCatAdmin,           // _In_  HCATADMIN hCatAdmin,
                IntPtr hCatInfo,            // _In_  HCATINFO hCatInfo,
                UInt32 dwFlags              // _In_  DWORD dwFlags
            );

            [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = true, CharSet = CharSet.Unicode)]
            public static extern Boolean CryptCATAdminReleaseContext(
                IntPtr hCatAdmin,           // _In_  HCATADMIN hCatAdmin,
                UInt32 dwFlags              // _In_  DWORD dwFlags
            );

            [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = false, CharSet = CharSet.Unicode)]
            //[return: MarshalAs(UnmanagedType.LPStruct)]
            //public static extern CryptProviderData WTHelperProvDataFromStateData(
            public static extern IntPtr WTHelperProvDataFromStateData(
                [In] IntPtr hStateData           // _In_  HANDLE hStateData
            );

            [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = false, CharSet = CharSet.Unicode)]
            public static extern IntPtr /* CRYPT_PROVIDER_SGNR */ WTHelperGetProvSignerFromChain(
                IntPtr pProvData,    // _In_  CRYPT_PROVIDER_DATA *pProvData,
                UInt32 idxSigner,           // _In_  DWORD idxSigner,
                Boolean fCounterSigner,     // _In_  BOOL fCounterSigner,
                UInt32 idxCounterSigner     // _In_  DWORD idxCounterSigner
            );

            [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = false, CharSet = CharSet.Unicode)]
            static extern WinVerifyTrustResult WinVerifyTrust(
                [In] IntPtr hwnd,           // _In_  HWND hWnd,
                [In] [MarshalAs(UnmanagedType.LPStruct)] Guid pgActionID,   // _In_  GUID *pgActionID,
                [In, Out] WinTrustData pWVTData  // _In_  LPVOID pWVTData
            );

            [DllImport("Crypt32.dll", EntryPoint = "CertNameToStrW", ExactSpelling = true, SetLastError = false, CharSet = CharSet.Unicode)]
            static extern UInt32 CertNameToStr(
                [In] UInt32 dwCertEncodingType,     // _In_   DWORD dwCertEncodingType,
                [In] IntPtr pName,                  // _In_   PCERT_NAME_BLOB pName,
                [In] UInt32 dwStrType,              // _In_   DWORD dwStrType,
                [In, Out] StringBuilder psz,                   // _Out_  LPTSTR psz,
                [In] UInt32 csz                     // _In_   DWORD csz
            );

            public const uint GENERIC_READ = 0x80000000;
            public const uint OPEN_EXISTING = 3;
            public const uint FILE_SHARE_READ = 0x00000001;
            [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
            static extern SafeFileHandle CreateFile(string lpFileName, uint dwDesiredAccess,
              uint dwShareMode, IntPtr lpSecurityAttributes, uint dwCreationDisposition,
              uint dwFlagsAndAttributes, IntPtr hTemplateFile);
            #endregion

            #region Check for function
            [DllImport("kernel32", SetLastError = true)]
            static extern IntPtr LoadLibrary(string lpFileName);

            [DllImport("kernel32", SetLastError = true)]
            static extern IntPtr GetProcAddress(IntPtr hModule, string lpFunction);


            static bool LibraryExists(string library)
            {
                return LoadLibrary(library) == IntPtr.Zero;
            }

            static bool FunctionExists(string library, string functionName)
            {
                IntPtr hModule = LoadLibrary(library);
                if (hModule == IntPtr.Zero) return false;
                IntPtr hFunction = GetProcAddress(hModule, functionName);
                if (hFunction == IntPtr.Zero) return false;
                return true;
            }
            #endregion

            #region Disable Wow64 redirection
            [DllImport("kernel32.dll", SetLastError = true)]
            public static extern bool Wow64DisableWow64FsRedirection(ref IntPtr ptr);
            [DllImport("kernel32.dll", SetLastError = true)]
            public static extern bool Wow64RevertWow64FsRedirection(IntPtr ptr);

            public static void DisableWow64Direction()
            {
                IntPtr ptr = new IntPtr();
                Wow64DisableWow64FsRedirection(ref ptr);

                //Wow64RevertWow64FsRedirection(ptr);
            }
            #endregion


            // Used to avoid any UI prompts
            private static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);
            // GUID of the action to perform
            private const string WINTRUST_ACTION_GENERIC_VERIFY_V2 = "{00AAC56B-CD44-11d0-8CC2-00C04FC295EE}";

            public static Signer GetSignerFromStateData(IntPtr StateData)
            {
                // Sanity check
                if (StateData == IntPtr.Zero)
                {
                    return null;
                }

                // 1. Get provider data from state data
                IntPtr pProvData = WTHelperProvDataFromStateData(StateData);
                if (pProvData == IntPtr.Zero)
                {
                    return null;
                }
                CryptProviderData provData = (CryptProviderData)Marshal.PtrToStructure(pProvData, typeof(CryptProviderData));

                // 2. Get provider signer from provider data
                IntPtr pSgnr = WTHelperGetProvSignerFromChain(pProvData, 0, false, 0);
                if (pSgnr == IntPtr.Zero)
                {
                    return null;
                }

                CryptProviderSgnr sgnr = (CryptProviderSgnr)Marshal.PtrToStructure(pSgnr, typeof(CryptProviderSgnr));
                if (sgnr.pasCertChain == null)
                {
                    return null;
                }
                if (sgnr.csCertChain == 0)
                {
                    return null;
                }

                // 3. Get provider cert from provider signer
                var providerCerts = new List<CryptProviderCert>();
                var ptr = sgnr.pasCertChain;
                int sizeof_cryptProviderCert = Marshal.SizeOf(new CryptProviderCert());

                // Collect certificate chain into a list
                for (int i = 0; i < sgnr.csCertChain; i++)
                {
                    providerCerts.Add((CryptProviderCert)Marshal.PtrToStructure(ptr, typeof(CryptProviderCert)));
                    ptr = (IntPtr)((int)ptr + sizeof_cryptProviderCert);

                    // Sanity check
                    const int MAX_CERT_CHAIN_LENGTH = 20; // Arbitrary max length of a chain I'm willing to use
                    if (i > MAX_CERT_CHAIN_LENGTH)
                    {
                        break;
                    }
                }

                // This is actually a list, but I only care about the first element
                CryptProviderCert cert = providerCerts[0];

                // 4. Get cert context
                CertContext certContext = (CertContext)Marshal.PtrToStructure(cert.pCert, typeof(CertContext));

                // 5. Get cert info 
                CertInfo certInfo = (CertInfo)Marshal.PtrToStructure(certContext.pCertInfo, typeof(CertInfo));
                if (certInfo == null)
                    return null;

                CRYPTOAPI_BLOB subject = certInfo.Subject;

                // 6. Get subject X.500 string
                string issuer = GetCertIssuerString(subject);

                // Get the best name for identifying this cert
                X500DistinguishedName x500DN = new X500DistinguishedName(issuer);
                string signerName = getBestName(x500DN);
                // Clean up the signer name
                signerName = signerName.Replace("\"", "");
                signerName = signerName.Trim();  // Remove trailing "\x0d"

                int serialNumberLen = certInfo.SerialNumber.cbData;
                if (serialNumberLen < 0 || serialNumberLen > 256) {
                    // TODO Should throw an error
                }
                var serialNumber = new byte[serialNumberLen]; 
                Marshal.Copy(certInfo.SerialNumber.pbData, serialNumber, 0, serialNumberLen);
                // Byte order seems to be reversed, so I'm flipping it here
                Array.Reverse(serialNumber, 0, serialNumberLen);


                var certEntity = new Certificate {
                    Version = certInfo.dwVersion,
                    Issuer = issuer,
                    SerialNumber = serialNumber,
                    DigestAlgorithm = certInfo.SignatureAlgorithm.pszObjId,
                    DigestEncryptionAlgorithm = certInfo.SubjectPublicKeyInfoAlgo.Algorithm.pszObjId
                };

                var signer = new Signer
                {
                    Name = signerName,
                    Timestamp = DateTime.FromFileTime((long)sgnr.sftVerifyAsOf),
                    SigningCert = certEntity
                };

                return signer;
            }

            public static string getBestName(X500DistinguishedName x500DN)
            {
                // Break the DN into parts
                var DNParts = ParseX500Subject(x500DN.Format(true));
                List<string> names;
                if (DNParts.TryGetValue("CN", out names))
                {
                    // Return the first CN found
                    return names[0];
                }
                else
                {
                    if (DNParts.TryGetValue("OU", out names))
                    {
                        return names[0];
                    }
                }
                // Else give up and return nothing
                return "";
            }

            public static Dictionary<string, List<string>> ParseX500Subject(string dn)
            {
                var parts = new Dictionary<string, List<string>>();
                if (dn == null) return parts;
                var lines = dn.Split('\n');

                foreach (var line in lines)
                {
                    int endOfKey = line.IndexOf("=");
                    if (endOfKey <= 0) continue;
                    string key = line.Substring(0, endOfKey);
                    string value = line.Substring(endOfKey + 1);

                    List<string> valueList;
                    if (parts.TryGetValue(key, out valueList))
                    {
                        valueList.Add(value);
                    }
                    else
                    {
                        valueList = new List<string>();
                        valueList.Add(value);
                        parts[key] = valueList;
                    }
                }
                return parts;
            }


            public const Int32 X509_ASN_ENCODING = 0x00000001;
            public const Int32 CERT_X500_NAME_STR = 3;

            /// <summary>
            /// Given a cert, extracts something like "C=US, S=Washington, L=Redmond, O=Microsoft Corporation, CN=Microsoft Corporation"
            /// </summary>
            /// <param name="blob"></param>
            /// <returns></returns>
            public static string GetCertIssuerString(CRYPTOAPI_BLOB blob)
            {
                StringBuilder sb;

                // Convert CRYPTOAPI_BLOB to unmanaged pointer
                IntPtr p = Marshal.AllocHGlobal(Marshal.SizeOf(blob));
                try
                {
                    Marshal.StructureToPtr(blob, p, false);

                    // Get size
                    UInt32 bufferSize = CertNameToStr(
                        X509_ASN_ENCODING,
                        p,
                        CERT_X500_NAME_STR,
                        null,
                        0);

                    // Create var of that size
                    sb = new StringBuilder((int)bufferSize);

                    // Get the data
                    CertNameToStr(
                        X509_ASN_ENCODING,
                        p,
                        CERT_X500_NAME_STR,
                        sb,
                        bufferSize);

                }
                finally
                {
                    Marshal.FreeHGlobal(p);
                }

                return sb.ToString();
            }


            /// <summary>
            /// Calls WinVerifyTrust() to check embedded file signature
            /// </summary>
            /// <param name="FileName"></param>
            /// <param name="SignerName"></param>
            /// <returns></returns>
            public static WinVerifyTrustResult VerifyEmbeddedSignature(string FileName, out List<Signer> Signers)
            {
                Signers = null;
                WinVerifyTrustResult result = WinVerifyTrustResult.FileNotSigned;
                WinTrustData wtd = new WinTrustData(FileName, false, null, null, IntPtr.Zero);
                wtd.dwStateAction = WinTrustDataStateAction.Verify;
                Guid guidAction = new Guid(WINTRUST_ACTION_GENERIC_VERIFY_V2);

                try
                {
                    try
                    {
                        Log.Debug("Getting embedded signature");
                        result = WinVerifyTrust(INVALID_HANDLE_VALUE, guidAction, wtd);
                        if (result != WinVerifyTrustResult.Success)
                        {
                            if (result != WinVerifyTrustResult.FileNotSigned)
                            {
                                // TODO We should handle this as this is weird
                                Log.Warn("Verification failed due to reason {0}", result);
                            }
                            return result;
                        }
                        
                        var signer = GetSignerFromStateData(wtd.hWVTStateData);
                        Signers = new List<Signer>();
                        Signers.Add(signer);
                    }
                    catch (Exception e)
                    {
                        Log.Exception(e, "Exception in VerifyEmbeddedSignature");
                    }
                }
                finally
                {
                    // Clean up
                    wtd.dwStateAction = WinTrustDataStateAction.Close;
                    WinVerifyTrust(INVALID_HANDLE_VALUE, guidAction, wtd);
                }
                return result;
            }


            /// <summary>
            /// Given a catalog file, extracts the signer name
            /// </summary>
            /// <param name="FileName"></param>
            /// <param name="SignerName"></param>
            /// <returns></returns>
            public static WinVerifyTrustResult VerifyCatalogFile(string FileName, string CatalogName, string Hash, out List<Signer> Signers, IntPtr hCatAdmin)
            {
                // Much of this comes from: http://forum.sysinternals.com/howto-verify-the-digital-signature-of-a-file_topic19247.html

                WinVerifyTrustResult result = WinVerifyTrustResult.FileNotSigned;
                WinTrustData wtd = new WinTrustData(FileName, true, Hash, CatalogName, hCatAdmin);
                wtd.dwStateAction = WinTrustDataStateAction.Verify;
                Guid guidAction = new Guid(WINTRUST_ACTION_GENERIC_VERIFY_V2);
                Signers = null;

                try
                {
                    result = WinVerifyTrust(INVALID_HANDLE_VALUE, guidAction, wtd);
                    if (result != WinVerifyTrustResult.Success)
                    {
                        if (result != WinVerifyTrustResult.FileNotSigned)
                        {
                            Log.Warn("Verification failed due to reason {0}", result);
                        }
                        return result;
                    }

                    var signer = GetSignerFromStateData(wtd.hWVTStateData);
                    Signers = new List<Signer>();
                    Signers.Add(signer);
                }
                finally
                {
                    // Clean up
                    wtd.dwStateAction = WinTrustDataStateAction.Close;
                    WinVerifyTrust(INVALID_HANDLE_VALUE, guidAction, wtd);
                }
                return result;
            }


            /// <summary>
            /// Looks in Microsoft's catalog files to verify a file
            /// </summary>
            /// <param name="FileName"></param>
            /// <param name="HashAlgorithm"></param>
            /// <param name="SignerName"></param>
            /// <returns></returns>
            public static WinVerifyTrustResult VerifyFileFromCatalog(string FileName, string HashAlgorithm, out List<Signer> Signers)
            {
                WinVerifyTrustResult result = WinVerifyTrustResult.FileNotSigned;
                Signers = null; // TODO MUST set this

                //
                // Check file is not too large
                //
                long length = new System.IO.FileInfo(FileName).Length;
                if (length > 32 * 1024 * 1024)
                {
                    // TODO IMPORTANT Do something better for large files
                    return WinVerifyTrustResult.FileNotSigned;
                }

                IntPtr phCatAdmin = IntPtr.Zero;
                Guid pgSubSystem = new Guid(WINTRUST_ACTION_GENERIC_VERIFY_V2);

                //
                // Get phCatAdmin handle.  First try using the Windows 8 function CryptCATAdminAcquireContext2, then use the normal function
                //
                if (FunctionExists("wintrust.dll", "CryptCATAdminAcquireContext2"))
                {
                    // Function exists, so use it.
                    if (!CryptCATAdminAcquireContext2(out phCatAdmin, IntPtr.Zero, HashAlgorithm, IntPtr.Zero, 0))
                    {
                        // Function exists, but could not be used, that is weird.
                        Log.Error("Call to CryptCATAdminAcquireContext2 failed with error {0}", Marshal.GetLastWin32Error());
                        return WinVerifyTrustResult.FileNotSigned;
                    }
                }
                else
                {
                    // New function does not exist, so use the old one.
                    if (!CryptCATAdminAcquireContext(out phCatAdmin, IntPtr.Zero, 0))
                    {
                        Log.Error("Call to CryptCATAdminAcquireContext failed with error {0}", Marshal.GetLastWin32Error());
                        return WinVerifyTrustResult.FileNotSigned;
                    }
                }

                //
                // Get handle to file
                //
                SafeFileHandle hFile = CreateFile(FileName, GENERIC_READ, FILE_SHARE_READ, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
                if (hFile.IsInvalid)
                {
                    Log.Error("Call to CreateFile failed with error {0}", Marshal.GetLastWin32Error());
                    return WinVerifyTrustResult.FileNotSigned;
                }

                //
                // Calc hash
                //
                UInt32 fileHashLength = 16;
                IntPtr fileHash = Marshal.AllocHGlobal((int)fileHashLength);
                if (FunctionExists("wintrust.dll", "CryptCATAdminCalcHashFromFileHandle2"))
                {
                    // Get size of the file hash to be used
                    if (!CryptCATAdminCalcHashFromFileHandle2(phCatAdmin, hFile, ref fileHashLength, fileHash, 0))
                    {
                        // Alloc the correct amount and try again
                        Marshal.FreeHGlobal(fileHash);
                        fileHash = Marshal.AllocHGlobal((int)fileHashLength);

                        if (!CryptCATAdminCalcHashFromFileHandle2(phCatAdmin, hFile, ref fileHashLength, fileHash, 0))
                        {
                            // Something went wrong
                            Log.Error("Call to CryptCATAdminCalcHashFromFileHandle2 failed with error {0}", Marshal.GetLastWin32Error());
                            // Clean up
                            CryptCATAdminReleaseContext(phCatAdmin, 0);
                            Marshal.FreeHGlobal(fileHash);
                            return WinVerifyTrustResult.FileNotSigned;
                        }
                    }
                }
                else
                {
                    // Get size of the file hash to be used
                    if (!CryptCATAdminCalcHashFromFileHandle(hFile, ref fileHashLength, fileHash, 0))
                    {
                        // Alloc the correct amount and try again
                        Marshal.FreeHGlobal(fileHash);
                        fileHash = Marshal.AllocHGlobal((int)fileHashLength);

                        if (!CryptCATAdminCalcHashFromFileHandle(hFile, ref fileHashLength, fileHash, 0))
                        {
                            // Something went wrong
                            Log.Error("Call to CryptCATAdminCalcHashFromFileHandle2 failed with error {0}", Marshal.GetLastWin32Error());
                            // Clean up
                            CryptCATAdminReleaseContext(phCatAdmin, 0);
                            Marshal.FreeHGlobal(fileHash);
                            return WinVerifyTrustResult.FileNotSigned;
                        }
                    }
                }

                // Close the file so we don't get a sharing violation later
                hFile.Close();

                IntPtr hCatInfo = IntPtr.Zero;
                hCatInfo = CryptCATAdminEnumCatalogFromHash(phCatAdmin, fileHash, fileHashLength, 0, ref hCatInfo);
                bool found = false;

                while (hCatInfo != IntPtr.Zero)
                {
                    IntPtr ci = Marshal.AllocHGlobal((int)Marshal.SizeOf(typeof(CatalogInfo)));
                    if (CryptCATCatalogInfoFromContext(hCatInfo, ci, 0))
                    {
                        CatalogInfo catInfo = (CatalogInfo)Marshal.PtrToStructure(ci, typeof(CatalogInfo));
                        string catalogFileName = catInfo.wszCatalogFile;

                        Database.LogCatalogFile(catalogFileName);

                        // Get the hash as a string
                        var fileHashByteArray = new byte[fileHashLength];
                        System.Runtime.InteropServices.Marshal.Copy(fileHash, fileHashByteArray, 0, (int)fileHashLength);
                        string fileHashTag = ByteArrayToString(fileHashByteArray);

                        //
                        // Use WinVerifyTrust to get the rest of the info
                        //
                        result = VerifyCatalogFile(FileName, catalogFileName, fileHashTag, out Signers, phCatAdmin);

                        found = true;
                        break;
                    }
                    else
                    {
                        Log.Error("Call to CryptCATCatalogInfoFromContext failed with error {0}", Marshal.GetLastWin32Error());
                    }

                    // Try the next catalog and loop again
                    hCatInfo = CryptCATAdminEnumCatalogFromHash(phCatAdmin, fileHash, fileHashLength, 0, ref hCatInfo);
                }

                if (!found)
                {
                    //Log.Info("Hash not found in any catalogs");
                }

                // Clean up
                // TODO Need to ensure these are always called
                CryptCATAdminReleaseCatalogContext(phCatAdmin, hCatInfo, 0);
                CryptCATAdminReleaseContext(phCatAdmin, 0);
                //hFile.Close();
                Marshal.FreeHGlobal(fileHash);

                return result;
            }


            /// <summary>
            /// Convert a byte array to a string
            /// </summary>
            /// <param name="ba"></param>
            /// <returns></returns>
            public static string ByteArrayToString(byte[] ba)
            {
                // From: http://stackoverflow.com/questions/311165/how-do-you-convert-byte-array-to-hexadecimal-string-and-vice-versa
                StringBuilder hex = new StringBuilder(ba.Length * 2);
                foreach (byte b in ba)
                    hex.AppendFormat("{0:X2}", b);
                return hex.ToString();
            }

            /// <summary>
            /// Checks if a file can be verified by it's code signature
            /// </summary>
            /// <param name="FileName"></param>
            /// <param name="SignerName"></param>
            /// <returns></returns>
            public static bool Verify(string FileName, out List<Signer> Signers)
            {
                WinVerifyTrustResult result = VerifyEmbeddedSignature(FileName, out Signers);
                if (result == WinVerifyTrustResult.FileNotSigned)
                {
                    // File may have been signed in a catalog, so check those.
                    // First look for a SHA256 signature
                    result = VerifyFileFromCatalog(FileName, "SHA256", out Signers);
                    if (result != WinVerifyTrustResult.Success)
                    {
                        // No SHA256 found, so look for whatever we can
                        result = VerifyFileFromCatalog(FileName, null, out Signers);
                    }
                }

                return result == WinVerifyTrustResult.Success;
            }

        }
    }
}
