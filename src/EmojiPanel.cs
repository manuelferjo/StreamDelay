// Botón 😊 del título: pone el cursor en el campo y abre el panel de emojis de Windows
// (Win + . / Win + ;), que escribe el emoji elegido donde está el cursor.
using System;
using System.Runtime.InteropServices;

namespace StreamDelay
{
	internal partial class MainForm
	{
		[DllImport("user32.dll")]
		private static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);

		private const byte VK_LWIN = 0x5B;

		private const byte VK_OEM_PERIOD = 0xBE;

		private const uint KEYEVENTF_KEYUP = 2u;

		private void OpenEmojiPanel(Field field)
		{
			field.EditNow();
			// Cuando el campo ya tiene el cursor: Win + . (el panel se abre junto a él).
			BeginInvoke((Action)delegate
			{
				keybd_event(VK_LWIN, 0, 0u, UIntPtr.Zero);
				keybd_event(VK_OEM_PERIOD, 0, 0u, UIntPtr.Zero);
				keybd_event(VK_OEM_PERIOD, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
				keybd_event(VK_LWIN, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
			});
		}
	}
}
