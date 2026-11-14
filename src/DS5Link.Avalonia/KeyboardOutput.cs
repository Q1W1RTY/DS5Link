using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace DS5Link;
internal static class KeyboardOutput {
 [StructLayout(LayoutKind.Sequential)]struct Keyboard {public ushort Key,Scan;public uint Flags,Time;public UIntPtr Extra;}
 [StructLayout(LayoutKind.Sequential)]struct Mouse {public int X,Y;public uint Data,Flags,Time;public UIntPtr Extra;}
 [StructLayout(LayoutKind.Explicit)]struct Data {[FieldOffset(0)]public Keyboard Keyboard;[FieldOffset(0)]public Mouse Mouse;}
 [StructLayout(LayoutKind.Sequential)]struct Input {public uint Type;public Data Data;}
 [DllImport("user32.dll",SetLastError=true)]static extern uint SendInput(uint count,Input[] inputs,int size);
 internal static void MoveMouse(int x,int y){
  if(x==0 && y==0)return;
  SendInput(1,new[]{new Input{Data=new Data{Mouse=new Mouse{X=x,Y=y,Flags=1}}}},Marshal.SizeOf<Input>());
 }
 [DllImport("user32.dll")]static extern short GetAsyncKeyState(int key);
 [DllImport("user32.dll")]static extern uint MapVirtualKey(uint key,uint type);
 [DllImport("user32.dll")]static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr window,out uint process);
 static readonly Dictionary<int,Input> held=new();
 static Input Make(int key){var scan=MapVirtualKey((uint)key,4);return new(){Type=1,Data=new Data {Keyboard=new Keyboard {Key=scan==0?(ushort)key:(ushort)0,Scan=(ushort)(scan&255),Flags=scan==0?0:8u|((scan&0xff00)!=0?1u:0u)}}};}
 internal static void ReleaseAll(){Release(held.Keys.Reverse().ToArray());}
 static bool Release(IEnumerable<int> keys){
  var own=keys.Where(held.ContainsKey).Select(k=>{var input=held[k];input.Data.Keyboard.Flags|=2;return (Key:k,Input:input);}).ToArray();
  if(own.Length==0)return true;
  var count=SendInput((uint)own.Length,own.Select(p=>p.Input).ToArray(),Marshal.SizeOf<Input>());
  foreach(var item in own.Take((int)count))held.Remove(item.Key);return count==own.Length;
 }
 internal static async Task<bool> SendAsync(KeyBinding binding) {
  if(!binding.Valid)return false;
  ReleaseAll();if(held.Count>0)return false;
  GetWindowThreadProcessId(GetForegroundWindow(),out var foreground);
  if(foreground==0 || foreground==(uint)Environment.ProcessId)return true;
  // Never release keys physically held by the user or accidentally add their modifiers.
  if(new[]{0x10,0x11,0x12,0x5B,0x5C,binding.VirtualKey}.Any(k=>(GetAsyncKeyState(k)&0x8000)!=0))return true;
  var keys=new List<int>();if((binding.Modifiers&2)!=0)keys.Add(0x11);if((binding.Modifiers&1)!=0)keys.Add(0x12);if((binding.Modifiers&4)!=0)keys.Add(0x10);keys.Add(binding.VirtualKey);
  var inputs=keys.Select(Make).ToArray();
  var count=SendInput((uint)inputs.Length,inputs,Marshal.SizeOf<Input>());
  for(int i=0;i<count;i++)held[keys[i]]=inputs[i];
  bool success=count==inputs.Length;
  try{if(success)await Task.Delay(40);}
  finally{success=Release(keys.AsEnumerable().Reverse()) && success;}
  return success;
 }
}
