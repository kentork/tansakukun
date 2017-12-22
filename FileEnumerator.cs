using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Runtime.InteropServices;
using System.IO;
using System.ComponentModel;

using TansakuKun.Data;
using TansakuKun.Native;

namespace TansakuKun
{
  public class FileEnumerator
  {

    private Dictionary<ulong, FileNameAndParentFrn> _directories = new Dictionary<ulong, FileNameAndParentFrn>();

    public Dictionary<ulong, FileNameAndParentFrn> Directories
    {
      get { return _directories; }
      set { _directories = value; }
    }

    private IntPtr _changeJournalRootHandle;

    public Dictionary<ulong, FileNameAndParentFrn> EnumerateVolume(string drive, string[] fileExtensions)
    {
      var files = new Dictionary<ulong, FileNameAndParentFrn>();
      var medBuffer = IntPtr.Zero;

      try
      {
        GetRootFrnEntry(drive);
        GetRootHandle(drive);

        CreateChangeJournal();

        SetupDataBuffer(ref medBuffer);
        EnumerateFiles(medBuffer, ref files, fileExtensions);
        ResolvePath(drive, ref files);

        return files;
      }
      catch (Exception e)
      {
        Console.Error.WriteLine(e.Message);
        Console.Error.WriteLine(e.StackTrace);
        Exception innerException = e.InnerException;
        while (innerException != null)
        {
          Console.Error.WriteLine(innerException.Message);
          Console.Error.WriteLine(innerException.StackTrace);
          innerException = innerException.InnerException;
        }
        throw new ApplicationException("Error in EnumerateVolume()", e);
      }
      finally
      {
        if (_changeJournalRootHandle.ToInt32() != NativeWrapper.INVALID_HANDLE_VALUE)
        {
          NativeWrapper.CloseHandle(_changeJournalRootHandle);
        }
        if (medBuffer != IntPtr.Zero)
        {
          Marshal.FreeHGlobal(medBuffer);
        }
      }
    }

    private void GetRootFrnEntry(string drive)
    {
      string driveRoot = string.Concat("\\\\.\\", drive);
      driveRoot = string.Concat(driveRoot, Path.DirectorySeparatorChar);
      IntPtr hRoot = NativeWrapper.CreateFile(driveRoot,
        0,
        NativeWrapper.FILE_SHARE_READ | NativeWrapper.FILE_SHARE_WRITE,
        IntPtr.Zero,
        NativeWrapper.OPEN_EXISTING,
        NativeWrapper.FILE_FLAG_BACKUP_SEMANTICS,
        IntPtr.Zero);

      if (hRoot.ToInt32() != NativeWrapper.INVALID_HANDLE_VALUE)
      {
        BY_HANDLE_FILE_INFORMATION fi = new BY_HANDLE_FILE_INFORMATION();
        bool bRtn = NativeWrapper.GetFileInformationByHandle(hRoot, out fi);
        if (bRtn)
        {
          UInt64 fileIndexHigh = (UInt64)fi.FileIndexHigh;
          UInt64 indexRoot = (fileIndexHigh << 32) | fi.FileIndexLow;

          FileNameAndParentFrn f = new FileNameAndParentFrn(driveRoot, 0);

          _directories.Add(indexRoot, f);
        }
        else
        {
          throw new IOException("GetFileInformationbyHandle() returned invalid handle",
            new Win32Exception(Marshal.GetLastWin32Error()));
        }
        NativeWrapper.CloseHandle(hRoot);
      }
      else
      {
        throw new IOException("Unable to get root frn entry", new Win32Exception(Marshal.GetLastWin32Error()));
      }
    }

    private void GetRootHandle(string drive)
    {
      string vol = string.Concat("\\\\.\\", drive);
      _changeJournalRootHandle = NativeWrapper.CreateFile(vol,
         NativeWrapper.GENERIC_READ | NativeWrapper.GENERIC_WRITE,
         NativeWrapper.FILE_SHARE_READ | NativeWrapper.FILE_SHARE_WRITE,
         IntPtr.Zero,
         NativeWrapper.OPEN_EXISTING,
         0,
         IntPtr.Zero);
      if (_changeJournalRootHandle.ToInt32() == NativeWrapper.INVALID_HANDLE_VALUE)
      {
        throw new IOException("CreateFile() returned invalid handle",
          new Win32Exception(Marshal.GetLastWin32Error()));
      }
    }

    unsafe private void CreateChangeJournal()
    {
      // This function creates a journal on the volume. If a journal already
      // exists this function will adjust the MaximumSize and AllocationDelta
      // parameters of the journal
      UInt64 MaximumSize = 0x800000;
      UInt64 AllocationDelta = 0x100000;
      UInt32 cb;
      CREATE_USN_JOURNAL_DATA cujd;
      cujd.MaximumSize = MaximumSize;
      cujd.AllocationDelta = AllocationDelta;

      int sizeCujd = Marshal.SizeOf(cujd);
      IntPtr cujdBuffer = Marshal.AllocHGlobal(sizeCujd);
      NativeWrapper.ZeroMemory(cujdBuffer, sizeCujd);
      Marshal.StructureToPtr(cujd, cujdBuffer, true);

      bool fOk = NativeWrapper.DeviceIoControl(_changeJournalRootHandle, NativeWrapper.FSCTL_CREATE_USN_JOURNAL,
        cujdBuffer, sizeCujd, IntPtr.Zero, 0, out cb, IntPtr.Zero);
      if (!fOk)
      {
        throw new IOException("DeviceIoControl() returned false", new Win32Exception(Marshal.GetLastWin32Error()));
      }
    }

    unsafe private void SetupDataBuffer(ref IntPtr medBuffer)
    {
      uint bytesReturned = 0;
      USN_JOURNAL_DATA ujd = new USN_JOURNAL_DATA();

      bool bOk = NativeWrapper.DeviceIoControl(_changeJournalRootHandle,                           // Handle to drive
        NativeWrapper.FSCTL_QUERY_USN_JOURNAL,   // IO Control Code
        IntPtr.Zero,                // In Buffer
        0,                          // In Buffer Size
        out ujd,                    // Out Buffer
        sizeof(USN_JOURNAL_DATA),  // Size Of Out Buffer
        out bytesReturned,          // Bytes Returned
        IntPtr.Zero);               // lpOverlapped
      if (bOk)
      {
        MFT_ENUM_DATA med;
        med.StartFileReferenceNumber = 0;
        med.LowUsn = 0;
        med.HighUsn = ujd.NextUsn;
        int sizeMftEnumData = Marshal.SizeOf(med);
        medBuffer = Marshal.AllocHGlobal(sizeMftEnumData);
        NativeWrapper.ZeroMemory(medBuffer, sizeMftEnumData);
        Marshal.StructureToPtr(med, medBuffer, true);
      }
      else
      {
        throw new IOException("DeviceIoControl() returned false", new Win32Exception(Marshal.GetLastWin32Error()));
      }
    }

    unsafe public void EnumerateFiles(IntPtr medBuffer, ref Dictionary<ulong, FileNameAndParentFrn> files, string[] fileExtensions)
    {
      IntPtr pData = Marshal.AllocHGlobal(sizeof(UInt64) + 0x10000);

      try
      {
        NativeWrapper.ZeroMemory(pData, sizeof(UInt64) + 0x10000);
        uint outBytesReturned = 0;

        while (false != NativeWrapper.DeviceIoControl(_changeJournalRootHandle,
                    NativeWrapper.FSCTL_ENUM_USN_DATA,
                    medBuffer,
                    sizeof(MFT_ENUM_DATA),
                    pData,
                    sizeof(UInt64) + 0x10000,
                    out outBytesReturned,
                    IntPtr.Zero))
        {
          IntPtr pUsnRecord = new IntPtr(pData.ToInt32() + sizeof(Int64));
          while (outBytesReturned > 60)
          {
            USN_RECORD usn = new USN_RECORD(pUsnRecord);
            if (0 != (usn.FileAttributes & NativeWrapper.FILE_ATTRIBUTE_DIRECTORY))
            {
              //
              // handle directories
              //
              if (!_directories.ContainsKey(usn.FileReferenceNumber))
              {
                _directories.Add(usn.FileReferenceNumber,
                  new FileNameAndParentFrn(usn.FileName, usn.ParentFileReferenceNumber));
              }
              else
              {   // this is debug code and should be removed when we are certain that
                  // duplicate frn's don't exist on a given drive.  To date, this exception has
                  // never been thrown.  Removing this code improves performance....
                throw new Exception(string.Format("Duplicate FRN: {0} for {1}",
                  usn.FileReferenceNumber, usn.FileName));
              }
            }
            else
            {
              //
              // handle files
              //

              // at this point we could get the * for the extension
              bool add = true;
              bool fullpath = false;
              if (fileExtensions != null && fileExtensions.Length != 0)
              {
                if (fileExtensions[0].ToString() == "*")
                {
                  add = true;
                  fullpath = true;
                }
                else
                {
                  add = false;
                  string s = Path.GetExtension(usn.FileName);
                  foreach (string extension in fileExtensions)
                  {
                    if (0 == string.Compare(s, extension, true))
                    {
                      add = true;
                      break;
                    }
                  }
                }
              }
              if (add)
              {
                if (fullpath)
                {
                  if (!files.ContainsKey(usn.FileReferenceNumber))
                  {
                    files.Add(usn.FileReferenceNumber,
                      new FileNameAndParentFrn(usn.FileName, usn.ParentFileReferenceNumber));
                  }
                  else
                  {
                    FileNameAndParentFrn frn = files[usn.FileReferenceNumber];
                    if (0 != string.Compare(usn.FileName, frn.Name, true))
                    {
                      //	Log.InfoFormat(
                      //	"Attempt to add duplicate file reference number: {0} for file {1}, file from index {2}",
                      //	usn.FileReferenceNumber, usn.FileName, frn.Name);
                      throw new Exception(string.Format("Duplicate FRN: {0} for {1}",
                        usn.FileReferenceNumber, usn.FileName));
                    }
                  }
                }
                else
                {
                  if (!files.ContainsKey(usn.FileReferenceNumber))
                  {
                    files.Add(usn.FileReferenceNumber,
                      new FileNameAndParentFrn(usn.FileName, usn.ParentFileReferenceNumber));
                  }
                  else
                  {
                    FileNameAndParentFrn frn = files[usn.FileReferenceNumber];
                    if (0 != string.Compare(usn.FileName, frn.Name, true))
                    {
                      //	Log.InfoFormat(
                      //	"Attempt to add duplicate file reference number: {0} for file {1}, file from index {2}",
                      //	usn.FileReferenceNumber, usn.FileName, frn.Name);
                      throw new Exception(string.Format("Duplicate FRN: {0} for {1}",
                        usn.FileReferenceNumber, usn.FileName));
                    }
                  }
                }
              }
            }
            pUsnRecord = new IntPtr(pUsnRecord.ToInt32() + usn.RecordLength);
            outBytesReturned -= usn.RecordLength;
          }
          Marshal.WriteInt64(medBuffer, Marshal.ReadInt64(pData, 0));
        }
      }
      catch (Exception e)
      {
        Console.Error.WriteLine(e.Message);
        Console.Error.WriteLine(e.StackTrace);
        throw new ApplicationException("Error in EnumerateFiles()", e);
      }
      finally
      {
        Marshal.FreeHGlobal(pData);
      }
    }

    private void ResolvePath(string drive, ref Dictionary<ulong, FileNameAndParentFrn> files)
    {
      foreach (KeyValuePair<ulong, FileNameAndParentFrn> entry in files)
      {
        FileNameAndParentFrn file = (FileNameAndParentFrn)entry.Value;
        file.Path = string.Concat(FrnToParentDirectory(drive, file.ParentFrn), Path.DirectorySeparatorChar, file.Name);
      }
    }
    private string FrnToParentDirectory(string drive, ulong frn)
    {
      if (!_directories.ContainsKey(frn)) return "";

      var parent = _directories[frn];
      if (parent.ParentFrn == 0) return drive;
      if (parent.Path != "")
      {
        return string.Concat(parent.Path, Path.DirectorySeparatorChar, parent.Name);
      }

      parent.Path = string.Concat(FrnToParentDirectory(drive, parent.ParentFrn), Path.DirectorySeparatorChar, parent.Name);
      return parent.Path;
    }
  }
}
