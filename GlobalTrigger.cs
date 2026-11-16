using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Threading;

namespace Voxlet;

public sealed class GlobalTrigger : IDisposable
{
    const int WH_KEYBOARD_LL=13, WH_MOUSE_LL=14, WM_KEYDOWN=0x100, WM_KEYUP=0x101, WM_SYSKEYDOWN=0x104, WM_SYSKEYUP=0x105, WM_XBUTTONDOWN=0x20B, WM_XBUTTONUP=0x20C;
    const uint XBUTTON1=1, XBUTTON2=2;
    delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);
    [StructLayout(LayoutKind.Sequential)] struct KBDLLHOOKSTRUCT { public uint vkCode, scanCode, flags, time; public UIntPtr extra; }
    [StructLayout(LayoutKind.Sequential)] struct MSLLHOOKSTRUCT { public System.Drawing.Point pt; public uint mouseData, flags, time; public UIntPtr extra; }
    [DllImport("user32.dll",SetLastError=true)] static extern IntPtr SetWindowsHookEx(int id,HookProc callback,IntPtr module,uint thread);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook,int code,IntPtr wParam,IntPtr lParam);
    [DllImport("kernel32.dll",CharSet=CharSet.Auto)] static extern IntPtr GetModuleHandle(string? name);
    readonly HookProc keyboardProc,mouseProc; IntPtr keyboard,mouse; readonly Dispatcher dispatcher; readonly Action down,up; readonly string trigger; readonly int vk; bool held;
    public GlobalTrigger(string binding,Dispatcher ui,Action pressed,Action released){trigger=binding;dispatcher=ui;down=pressed;up=released;keyboardProc=KeyboardHook;mouseProc=MouseHook;vk=ParseVk(binding);var module=GetModuleHandle(Process.GetCurrentProcess().MainModule?.ModuleName);keyboard=SetWindowsHookEx(WH_KEYBOARD_LL,keyboardProc,module,0);mouse=SetWindowsHookEx(WH_MOUSE_LL,mouseProc,module,0);}
    static int ParseVk(string key){if(key.StartsWith("D")&&key.Length==2&&char.IsDigit(key[1]))return key[1];if(key.StartsWith("NumPad")&&int.TryParse(key[6..],out var n))return 0x60+n;if(Enum.TryParse<Key>(key,out var wpf))return KeyInterop.VirtualKeyFromKey(wpf);return 0;}
    IntPtr KeyboardHook(int code,IntPtr wp,IntPtr lp){if(code>=0&&vk!=0){var k=Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lp);if(k.vkCode==vk){if(wp.ToInt32() is WM_KEYDOWN or WM_SYSKEYDOWN){if(!held){held=true;dispatcher.BeginInvoke(down);}}else if(wp.ToInt32() is WM_KEYUP or WM_SYSKEYUP){if(held){held=false;dispatcher.BeginInvoke(up);}}}}return CallNextHookEx(keyboard,code,wp,lp);}
    IntPtr MouseHook(int code,IntPtr wp,IntPtr lp){if(code>=0&&(trigger=="Mouse 4"||trigger=="Mouse 5")&&wp.ToInt32() is WM_XBUTTONDOWN or WM_XBUTTONUP){var m=Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lp);var button=(m.mouseData>>16)==XBUTTON1?"Mouse 4":"Mouse 5";if(button==trigger){if(wp.ToInt32()==WM_XBUTTONDOWN&&!held){held=true;dispatcher.BeginInvoke(down);}else if(wp.ToInt32()==WM_XBUTTONUP&&held){held=false;dispatcher.BeginInvoke(up);}}}return CallNextHookEx(mouse,code,wp,lp);}
    public void Dispose(){if(keyboard!=IntPtr.Zero)UnhookWindowsHookEx(keyboard);if(mouse!=IntPtr.Zero)UnhookWindowsHookEx(mouse);}
}
