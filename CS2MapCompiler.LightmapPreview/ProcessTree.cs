namespace CS2MapCompiler.LightmapPreview;

// The app only runs one compile at a time, but Hammer or another copy of the app can be baking too, so this makes sure we
// read the vrad3 our compile started
internal static class ProcessTree
{
    public static bool IsDescendantOf(int processId, int ancestorId)
    {
        // resourcecompiler starts vrad3 either directly or through a helper, so a few levels up is enough
        for (var depth = 0; depth < 8; depth++)
        {
            var parent = ParentOf(processId);

            if (parent == ancestorId)
            {
                return true;
            }

            if (parent <= 0 || parent == processId)
            {
                return false;
            }

            processId = parent;
        }

        return false;
    }

    private static unsafe int ParentOf(int processId)
    {
        var handle = NativeMethods.OpenProcess(NativeMethods.ProcessQueryLimitedInformation, false, processId);

        if (handle == 0)
        {
            return 0;
        }

        try
        {
            var status = NativeMethods.NtQueryInformationProcess(handle, 0, out var information, sizeof(NativeMethods.ProcessBasicInformation), out _);
            return status == 0 ? (int)information.InheritedFromUniqueProcessId : 0;
        }
        finally
        {
            NativeMethods.CloseHandle(handle);
        }
    }
}
