using System;
using System.Runtime.InteropServices;

namespace MacroDeck
{
    internal interface IFolderPicker { string PickFolder(IntPtr owner, string currentPath); }

    internal sealed class WindowsFolderPicker : IFolderPicker
    {
        private const uint PickFolders = 0x00000020;
        private const uint ForceFileSystem = 0x00000040;
        private const uint PathMustExist = 0x00000800;
        private const uint FileSystemPath = 0x80058000;
        private static readonly Guid OpenDialogClass = new Guid("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7");
        private static readonly Guid OpenDialogInterface = new Guid("D57C7288-D4AD-4768-BE02-9D969532D960");

        public string PickFolder(IntPtr owner, string currentPath)
        {
            IFileOpenDialog dialog = (IFileOpenDialog)Activator.CreateInstance(Type.GetTypeFromCLSID(OpenDialogClass));
            try
            {
                uint options; dialog.GetOptions(out options); dialog.SetOptions(options | PickFolders | ForceFileSystem | PathMustExist);
                dialog.SetTitle("Escolha onde organizar seus macros"); dialog.SetOkButtonLabel("Selecionar pasta");
                if (!String.IsNullOrWhiteSpace(currentPath) && System.IO.Directory.Exists(currentPath))
                {
                    IShellItem folder;
                    Guid shellItemId = typeof(IShellItem).GUID;
                    if (SHCreateItemFromParsingName(currentPath, IntPtr.Zero, ref shellItemId, out folder) == 0 && folder != null)
                    {
                        try { dialog.SetFolder(folder); } finally { Marshal.ReleaseComObject(folder); }
                    }
                }
                try { dialog.Show(owner); }
                catch (COMException ex) { if ((uint)ex.ErrorCode == 0x800704C7) return null; throw; }
                IShellItem result; dialog.GetResult(out result);
                try { IntPtr path; result.GetDisplayName(FileSystemPath, out path); try { return Marshal.PtrToStringUni(path); } finally { Marshal.FreeCoTaskMem(path); } }
                finally { Marshal.ReleaseComObject(result); }
            }
            finally { Marshal.ReleaseComObject(dialog); }
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
        private static extern int SHCreateItemFromParsingName(string path, IntPtr bindContext, [In] ref Guid interfaceId, out IShellItem item);

        [ComImport, Guid("D57C7288-D4AD-4768-BE02-9D969532D960"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFileOpenDialog
        {
            void Show(IntPtr parent);
            void SetFileTypes(uint count, IntPtr filters);
            void SetFileTypeIndex(uint index);
            void GetFileTypeIndex(out uint index);
            void Advise(IntPtr sink, out uint cookie);
            void Unadvise(uint cookie);
            void SetOptions(uint options);
            void GetOptions(out uint options);
            void SetDefaultFolder(IShellItem item);
            void SetFolder(IShellItem item);
            void GetFolder(out IShellItem item);
            void GetCurrentSelection(out IShellItem item);
            void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string name);
            void GetFileName(out IntPtr name);
            void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);
            void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string text);
            void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string label);
            void GetResult(out IShellItem item);
            void AddPlace(IShellItem item, int alignment);
            void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string extension);
            void Close(int hr);
            void SetClientGuid(ref Guid guid);
            void ClearClientData();
            void SetFilter(IntPtr filter);
            void GetResults(out IntPtr items);
            void GetSelectedItems(out IntPtr items);
        }

        [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItem
        {
            void BindToHandler(IntPtr bindContext, ref Guid handler, ref Guid interfaceId, out IntPtr result);
            void GetParent(out IShellItem parent);
            void GetDisplayName(uint sigdnName, out IntPtr name);
            void GetAttributes(uint mask, out uint attributes);
            void Compare(IShellItem item, uint hint, out int order);
        }
    }
}
