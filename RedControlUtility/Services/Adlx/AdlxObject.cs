using System.Runtime.InteropServices;

namespace RedControlUtility.Services.Adlx;

public sealed unsafe class AdlxObject : IDisposable
{
    private IntPtr _ptr;
    private readonly bool _refCounted;

    public AdlxObject(IntPtr ptr, bool refCounted = true)
    {
        _ptr = ptr;
        _refCounted = refCounted;
    }

    public IntPtr Ptr => _ptr;
    public bool IsValid => _ptr != IntPtr.Zero;

    private void** Vtbl => *(void***)_ptr;

    public void Dispose()
    {
        if (_ptr == IntPtr.Zero)
        {
            return;
        }

        IntPtr p = _ptr;
        _ptr = IntPtr.Zero;
        if (_refCounted)
        {
            try
            {
                var release = (delegate* unmanaged[Stdcall]<IntPtr, uint>)(*(void***)p)[1];
                release(p);
            }
            catch
            {
            }
        }
    }

    public AdlxObject? Query(string interfaceName, int slot = 2)
    {
        if (_ptr == IntPtr.Zero)
        {
            return null;
        }

        IntPtr outPtr = IntPtr.Zero;
        AdlxResult r;
        fixed (char* name = interfaceName)
        {
            var fn = (delegate* unmanaged[Stdcall]<IntPtr, char*, IntPtr*, int>)Vtbl[slot];
            r = (AdlxResult)fn(_ptr, name, &outPtr);
        }

        return r.Succeeded() && outPtr != IntPtr.Zero ? new AdlxObject(outPtr) : null;
    }

    public AdlxResult Invoke(int slot)
    {
        if (_ptr == IntPtr.Zero) return AdlxResult.InvalidObject;
        var fn = (delegate* unmanaged[Stdcall]<IntPtr, int>)Vtbl[slot];
        return (AdlxResult)fn(_ptr);
    }

    public AdlxResult GetInt(int slot, out int value)
    {
        value = 0;
        if (_ptr == IntPtr.Zero) return AdlxResult.InvalidObject;
        int v = 0;
        var fn = (delegate* unmanaged[Stdcall]<IntPtr, int*, int>)Vtbl[slot];
        var r = (AdlxResult)fn(_ptr, &v);
        value = v;
        return r;
    }

    public AdlxResult GetUInt(int slot, out uint value)
    {
        value = 0;
        if (_ptr == IntPtr.Zero) return AdlxResult.InvalidObject;
        uint v = 0;
        var fn = (delegate* unmanaged[Stdcall]<IntPtr, uint*, int>)Vtbl[slot];
        var r = (AdlxResult)fn(_ptr, &v);
        value = v;
        return r;
    }

    public AdlxResult GetDouble(int slot, out double value)
    {
        value = 0;
        if (_ptr == IntPtr.Zero) return AdlxResult.InvalidObject;
        double v = 0;
        var fn = (delegate* unmanaged[Stdcall]<IntPtr, double*, int>)Vtbl[slot];
        var r = (AdlxResult)fn(_ptr, &v);
        value = v;
        return r;
    }

    public AdlxResult GetBool(int slot, out bool value)
    {
        value = false;
        if (_ptr == IntPtr.Zero) return AdlxResult.InvalidObject;
        byte v = 0;
        var fn = (delegate* unmanaged[Stdcall]<IntPtr, byte*, int>)Vtbl[slot];
        var r = (AdlxResult)fn(_ptr, &v);
        value = v != 0;
        return r;
    }

    public AdlxResult GetPtr(int slot, out IntPtr value)
    {
        value = IntPtr.Zero;
        if (_ptr == IntPtr.Zero) return AdlxResult.InvalidObject;
        IntPtr v = IntPtr.Zero;
        var fn = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)Vtbl[slot];
        var r = (AdlxResult)fn(_ptr, &v);
        value = v;
        return r;
    }

    public AdlxResult GetString(int slot, out string value)
    {
        var r = GetPtr(slot, out IntPtr p);
        value = r.Succeeded() && p != IntPtr.Zero ? (Marshal.PtrToStringAnsi(p) ?? string.Empty) : string.Empty;
        return r;
    }

    public AdlxResult GetRange(int slot, out AdlxRange range)
    {
        range = AdlxRange.Empty;
        if (_ptr == IntPtr.Zero) return AdlxResult.InvalidObject;
        int* buf = stackalloc int[3];
        buf[0] = buf[1] = buf[2] = 0;
        var fn = (delegate* unmanaged[Stdcall]<IntPtr, int*, int>)Vtbl[slot];
        var r = (AdlxResult)fn(_ptr, buf);
        range = new AdlxRange(buf[0], buf[1], buf[2] <= 0 ? 1 : buf[2]);
        return r;
    }

    public AdlxResult GetRange2(int slot, out AdlxRange first, out AdlxRange second)
    {
        first = second = AdlxRange.Empty;
        if (_ptr == IntPtr.Zero) return AdlxResult.InvalidObject;
        int* a = stackalloc int[3];
        int* b = stackalloc int[3];
        for (int i = 0; i < 3; i++) { a[i] = 0; b[i] = 0; }
        var fn = (delegate* unmanaged[Stdcall]<IntPtr, int*, int*, int>)Vtbl[slot];
        var r = (AdlxResult)fn(_ptr, a, b);
        first = new AdlxRange(a[0], a[1], a[2] <= 0 ? 1 : a[2]);
        second = new AdlxRange(b[0], b[1], b[2] <= 0 ? 1 : b[2]);
        return r;
    }

    public AdlxResult GetInt2(int slot, out int a, out int b)
    {
        a = b = 0;
        if (_ptr == IntPtr.Zero) return AdlxResult.InvalidObject;
        int x = 0, y = 0;
        var fn = (delegate* unmanaged[Stdcall]<IntPtr, int*, int*, int>)Vtbl[slot];
        var r = (AdlxResult)fn(_ptr, &x, &y);
        a = x; b = y;
        return r;
    }

    public AdlxResult GetString3(int slot, out string a, out string b, out string c)
    {
        a = b = c = string.Empty;
        if (_ptr == IntPtr.Zero) return AdlxResult.InvalidObject;
        IntPtr x = IntPtr.Zero, y = IntPtr.Zero, z = IntPtr.Zero;
        var fn = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, IntPtr*, IntPtr*, int>)Vtbl[slot];
        var r = (AdlxResult)fn(_ptr, &x, &y, &z);
        if (r.Succeeded())
        {
            a = x != IntPtr.Zero ? Marshal.PtrToStringAnsi(x) ?? string.Empty : string.Empty;
            b = y != IntPtr.Zero ? Marshal.PtrToStringAnsi(y) ?? string.Empty : string.Empty;
            c = z != IntPtr.Zero ? Marshal.PtrToStringAnsi(z) ?? string.Empty : string.Empty;
        }
        return r;
    }

    public AdlxResult SetInt(int slot, int value)
    {
        if (_ptr == IntPtr.Zero) return AdlxResult.InvalidObject;
        var fn = (delegate* unmanaged[Stdcall]<IntPtr, int, int>)Vtbl[slot];
        return (AdlxResult)fn(_ptr, value);
    }

    public AdlxResult SetBool(int slot, bool value)
    {
        if (_ptr == IntPtr.Zero) return AdlxResult.InvalidObject;
        var fn = (delegate* unmanaged[Stdcall]<IntPtr, byte, int>)Vtbl[slot];
        return (AdlxResult)fn(_ptr, value ? (byte)1 : (byte)0);
    }

    public AdlxResult SetPtr(int slot, IntPtr value)
    {
        if (_ptr == IntPtr.Zero) return AdlxResult.InvalidObject;
        var fn = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int>)Vtbl[slot];
        return (AdlxResult)fn(_ptr, value);
    }

    public AdlxResult InvokeWith(int slot, IntPtr arg)
    {
        if (_ptr == IntPtr.Zero) return AdlxResult.InvalidObject;
        var fn = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int>)Vtbl[slot];
        return (AdlxResult)fn(_ptr, arg);
    }

    public AdlxResult GetBoolWith(int slot, IntPtr arg, out bool value)
    {
        value = false;
        if (_ptr == IntPtr.Zero) return AdlxResult.InvalidObject;
        byte v = 0;
        var fn = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, byte*, int>)Vtbl[slot];
        var r = (AdlxResult)fn(_ptr, arg, &v);
        value = v != 0;
        return r;
    }

    public AdlxResult GetPtrWith(int slot, IntPtr arg, out IntPtr value)
    {
        value = IntPtr.Zero;
        if (_ptr == IntPtr.Zero) return AdlxResult.InvalidObject;
        IntPtr v = IntPtr.Zero;
        var fn = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr*, int>)Vtbl[slot];
        var r = (AdlxResult)fn(_ptr, arg, &v);
        value = v;
        return r;
    }

    public AdlxResult ValidateStates(int slot, IntPtr list, out int errorIndex)
    {
        errorIndex = -1;
        if (_ptr == IntPtr.Zero) return AdlxResult.InvalidObject;
        int e = -1;
        var fn = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int*, int>)Vtbl[slot];
        var r = (AdlxResult)fn(_ptr, list, &e);
        errorIndex = e;
        return r;
    }

    public uint ListSize()
    {
        if (_ptr == IntPtr.Zero) return 0;
        var fn = (delegate* unmanaged[Stdcall]<IntPtr, uint>)Vtbl[3];
        return fn(_ptr);
    }

    public AdlxResult ListAt(uint index, out AdlxObject? item, int slot = 7)
    {
        item = null;
        if (_ptr == IntPtr.Zero) return AdlxResult.InvalidObject;
        IntPtr p = IntPtr.Zero;
        var fn = (delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr*, int>)Vtbl[slot];
        var r = (AdlxResult)fn(_ptr, index, &p);
        if (r.Succeeded() && p != IntPtr.Zero)
        {
            item = new AdlxObject(p);
        }
        return r;
    }

    public uint ListBegin()
    {
        if (_ptr == IntPtr.Zero) return 0;
        var fn = (delegate* unmanaged[Stdcall]<IntPtr, uint>)Vtbl[5];
        return fn(_ptr);
    }

    public uint ListEnd()
    {
        if (_ptr == IntPtr.Zero) return 0;
        var fn = (delegate* unmanaged[Stdcall]<IntPtr, uint>)Vtbl[6];
        return fn(_ptr);
    }

    public List<AdlxObject> ListItems(System.Text.StringBuilder? log = null, int typedAtSlot = 11)
    {
        var items = new List<AdlxObject>();
        uint n = ListSize();
        uint b = ListBegin();
        uint e = ListEnd();
        log?.AppendLine($"[ADLX] lista: Size={n} Begin={b} End={e}");

        for (uint loc = b; loc < e && loc < b + 256; loc++)
        {
            var r = ListAt(loc, out var item);
            log?.AppendLine($"[ADLX] At({loc}) => {r}");
            if (r.Succeeded() && item != null) items.Add(item);
        }

        if (items.Count == 0)
        {
            for (uint loc = 0; loc < n && loc < 256; loc++)
            {
                var r = ListAt(loc, out var item);
                log?.AppendLine($"[ADLX] At({loc}) (0..Size-1) => {r}");
                if (r.Succeeded() && item != null) items.Add(item);
            }
        }

        if (items.Count == 0 && typedAtSlot > 0)
        {
            for (uint loc = 0; loc < Math.Max(n, e) && loc < 256; loc++)
            {
                var r = ListAt(loc, out var item, typedAtSlot);
                log?.AppendLine($"[ADLX] At tipado (slot {typedAtSlot})({loc}) => {r}");
                if (r.Succeeded() && item != null) items.Add(item);
            }
        }

        return items;
    }

    public bool Supported(int slot = 3) => GetBool(slot, out bool v).Succeeded() && v;
}
