
#region ================== Copyright (c) 2007 Pascal vd Heiden

/*
 * Copyright (c) 2007 Pascal vd Heiden, www.codeimp.com
 * This program is released under GNU General Public License
 * 
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 * 
 */

#endregion

#region ================== Namespaces

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

#endregion

namespace CodeImp.DoomBuilder.Controls
{
	internal class Action
	{
		#region ================== Variables

		// Description
		private string name;
		private string shortname;
		private string title;
		private string description;

		// Shortcut key
		private int key;

		// Shortcut options
		private bool allowkeys;
		private bool allowmouse;
		private bool allowscroll;
		
		// Delegate
		private List<ActionDelegate> delegates;
		
		#endregion

		#region ================== Properties

		public string Name { get { return name; } }
		public string ShortName { get { return shortname; } }
		public string Title { get { return title; } }
		public string Description { get { return description; } }
		public int ShortcutKey { get { return key; } }
		public bool AllowKeys { get { return allowkeys; } }
		public bool AllowMouse { get { return allowmouse; } }
		public bool AllowScroll { get { return allowscroll; } }

		#endregion

		#region ================== Constructor / Disposer

		// Constructor
		public Action(string name, string shortname, string title, string description, int key,
					  bool allowkeys, bool allowmouse, bool allowscroll)
		{
			// Initialize
			this.name = name;
			this.shortname = shortname;
			this.title = title;
			this.description = description;
			this.delegates = new List<ActionDelegate>();
			this.allowkeys = allowkeys;
			this.allowmouse = allowmouse;
			this.allowscroll = allowscroll;
			this.key = key;
		}

		// Destructor
		~Action()
		{
			// Moo.
		}
		
		#endregion

		#region ================== Static Methods

		// This returns the shortcut key description for a key
		public static string GetShortcutKeyDesc(int key)
		{
			KeysConverter conv = new KeysConverter();
			int ctrl, button;
			string ctrlprefix = "";
			
			// When key is 0, then return an empty string
			if(key == 0) return "";

			// Split the key in Control and Button
			ctrl = key & ((int)Keys.Control | (int)Keys.Shift | (int)Keys.Alt);
			button = key & ~((int)Keys.Control | (int)Keys.Shift | (int)Keys.Alt);

			// When the button is a control key, then remove the control itsself
			if((button == (int)Keys.ControlKey) ||
			   (button == (int)Keys.ShiftKey))
			{
				ctrl = 0;
				key = key & ~((int)Keys.Control | (int)Keys.Shift | (int)Keys.Alt);
			}
			
			// Determine control prefix
			if(ctrl != 0) ctrlprefix = conv.ConvertToString(key);
			
			// Check if button is special
			switch(button)
			{
				// Scroll down
				case (int)SpecialKeys.MScrollDown:
					
					// Make string representation
					return ctrlprefix + "ScrollDown";

				// Scroll up
				case (int)SpecialKeys.MScrollUp:

					// Make string representation
					return ctrlprefix + "ScrollUp";

				// Keys that would otherwise have odd names
				case (int)Keys.Oemtilde: return ctrlprefix + "~";
				case (int)Keys.OemMinus: return ctrlprefix + "-";
				case (int)Keys.Oemplus: return ctrlprefix + "+";
				case (int)Keys.Subtract: return ctrlprefix + "NumPad-";
				case (int)Keys.Add: return ctrlprefix + "NumPad+";
				case (int)Keys.Decimal: return ctrlprefix + "NumPad.";
				case (int)Keys.Multiply: return ctrlprefix + "NumPad*";
				case (int)Keys.Divide: return ctrlprefix + "NumPad/";
				case (int)Keys.OemOpenBrackets: return ctrlprefix + "[";
				case (int)Keys.OemCloseBrackets: return ctrlprefix + "]";
				case (int)Keys.Oem1: return ctrlprefix + ";";
				case (int)Keys.Oem7: return ctrlprefix + "'";
				case (int)Keys.Oemcomma: return ctrlprefix + ",";
				case (int)Keys.OemPeriod: return ctrlprefix + ".";
				case (int)Keys.OemQuestion: return ctrlprefix + "?";
				case (int)Keys.Oem5: return ctrlprefix + "\\";
				case (int)Keys.Capital: return ctrlprefix + "CapsLock";
				case (int)Keys.Back: return ctrlprefix + "Backspace";
				
				default:
					
					// Use standard key-string conversion
					return conv.ConvertToString(key);
			}
		}

		#endregion

		#region ================== Methods

		// This sets a new key for the action
		public void SetShortcutKey(int key)
		{
			// Make it so.
			this.key = key;
		}
		
		// This binds a delegate to this action
		public void Bind(ActionDelegate method)
		{
			delegates.Add(method);
		}

		// This removes a delegate from this action
		public void Unbind(ActionDelegate method)
		{
			delegates.Remove(method);
		}

		// This raises events for this action
		public void Invoke()
		{
			List<ActionDelegate> delegateslist;
			
			// No method bound?
			if(delegates.Count == 0)
			{
				// Ignore this since keys can also be handled through KeyDown and KeyUp in editing modes
				//General.WriteLogLine("Called action '" + name + "' has no methods bound");
			}
			else
			{
				// Copy delegates list
				delegateslist = new List<ActionDelegate>(delegates);
				
				// Invoke all the delegates
				foreach(ActionDelegate ad in delegateslist) ad.Invoke();
			}
		}

		#endregion
	}
}
