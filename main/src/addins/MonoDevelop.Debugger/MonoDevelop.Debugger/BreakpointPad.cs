// BreakpointPad.cs
//
// Author:
//   Alfonso Santos Luaces <asantosluaces@gmail.com>
//
// Copyright (c) 2008 Alfonso Santos Luaces
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.

using Gtk;
using System;

using Stock = MonoDevelop.Ide.Gui.Stock;

using MonoDevelop.Core;
using MonoDevelop.Ide.Gui;
using MonoDevelop.Ide.Commands;
using MonoDevelop.Components;
using MonoDevelop.Components.Commands;
using Mono.Debugging.Client;
using MonoDevelop.Components.Docking;
using MonoDevelop.Ide.Gui.Components;
using MonoDevelop.Ide;

namespace MonoDevelop.Debugger
{
	public class BreakpointPad : IPadContent
	{
		BreakpointStore breakpoints;
		
		PadTreeView tree;
		TreeStore store;
		Widget control;
		ScrolledWindow sw;
		CommandEntrySet menuSet;
		TreeViewState treeState;
		
		EventHandler<BreakpointEventArgs> breakpointUpdatedHandler;
		EventHandler<BreakpointEventArgs> breakpointRemovedHandler;
		EventHandler<BreakpointEventArgs> breakpointAddedHandler;
		EventHandler breakpointChangedHandler;
		
		enum Columns
		{
			Icon,
			Selected,
			FileName,
			Breakpoint,
			Condition,
			TraceExp,
			HitCount,
			LastTrace
		}
		
		enum LocalCommands
		{
			GoToFile,
			Properties
		}
		
		public void Initialize (IPadWindow window)
		{
			// Toolbar and menu definitions
			
			ActionCommand gotoCmd = new ActionCommand (LocalCommands.GoToFile, GettextCatalog.GetString ("Go to File"));
			ActionCommand propertiesCmd = new ActionCommand (LocalCommands.Properties, GettextCatalog.GetString ("Properties"), Gtk.Stock.Properties);
			
			menuSet = new CommandEntrySet ();
			menuSet.Add (gotoCmd);
			menuSet.AddSeparator ();
			menuSet.AddItem (DebugCommands.EnableDisableBreakpoint);
			menuSet.AddItem (DebugCommands.ClearAllBreakpoints);
			menuSet.AddItem (DebugCommands.DisableAllBreakpoints);
			menuSet.AddItem (EditCommands.DeleteKey);
			menuSet.AddSeparator ();
			menuSet.Add (propertiesCmd);
			
			CommandEntrySet toolbarSet = new CommandEntrySet ();
			toolbarSet.AddItem (DebugCommands.EnableDisableBreakpoint);
			toolbarSet.AddItem (DebugCommands.ClearAllBreakpoints);
			toolbarSet.AddItem (DebugCommands.DisableAllBreakpoints);
			toolbarSet.AddItem (EditCommands.Delete);
			toolbarSet.AddSeparator ();
			toolbarSet.Add (propertiesCmd);
			
			// The breakpoint list
			
			store = new TreeStore (typeof(string), typeof (bool), typeof(string), typeof(object), typeof(string), typeof(string), typeof(string), typeof(string));

			tree = new PadTreeView ();
			tree.Model = store;
			tree.RulesHint = true;
			tree.HeadersVisible = true;
			tree.DoPopupMenu = ShowPopup;
			tree.KeyPressEvent += OnKeyPressEvent;
			tree.Selection.Mode = SelectionMode.Multiple;
			
			treeState = new TreeViewState (tree, (int) Columns.Breakpoint);
							
			TreeViewColumn col = new TreeViewColumn ();
			CellRenderer crp = new CellRendererImage ();
			col.PackStart (crp, false);
			col.AddAttribute (crp, "stock_id", (int) Columns.Icon);
			tree.AppendColumn (col);
			
			Gtk.CellRendererToggle toggleRender = new Gtk.CellRendererToggle ();
			toggleRender.Toggled += new ToggledHandler (ItemToggled);
			col = new TreeViewColumn ();
			col.PackStart (toggleRender, false);
			col.AddAttribute (toggleRender, "active", (int) Columns.Selected);
			tree.AppendColumn (col);
			
			TreeViewColumn FrameCol = new TreeViewColumn ();
			CellRenderer crt = tree.TextRenderer;
			FrameCol.Title = GettextCatalog.GetString ("Name");
			FrameCol.PackStart (crt, true);
			FrameCol.AddAttribute (crt, "text", (int) Columns.FileName);
			FrameCol.Resizable = true;
			FrameCol.Alignment = 0.0f;
			tree.AppendColumn (FrameCol);

			col = tree.AppendColumn (GettextCatalog.GetString ("Condition"), crt, "text", (int) Columns.Condition);
			col.Resizable = true;
			
			col = tree.AppendColumn (GettextCatalog.GetString ("Trace Expression"), crt, "text", (int) Columns.TraceExp);
			col.Resizable = true;
			
			col = tree.AppendColumn (GettextCatalog.GetString ("Hit Count"), crt, "text", (int) Columns.HitCount);
			col.Resizable = true;
			
			col = tree.AppendColumn (GettextCatalog.GetString ("Last Trace"), crt, "text", (int) Columns.LastTrace);
			col.Resizable = true;
			
			sw = new Gtk.ScrolledWindow ();
			sw.ShadowType = ShadowType.None;
			sw.Add (tree);
			
			control = sw;
			
			control.ShowAll ();
			
			breakpoints = DebuggingService.Breakpoints;
			
			UpdateDisplay ();

			breakpointUpdatedHandler = DispatchService.GuiDispatch<EventHandler<BreakpointEventArgs>> (OnBreakpointUpdated);
			breakpointRemovedHandler = DispatchService.GuiDispatch<EventHandler<BreakpointEventArgs>> (OnBreakpointRemoved);
			breakpointAddedHandler = DispatchService.GuiDispatch<EventHandler<BreakpointEventArgs>> (OnBreakpointAdded);
			breakpointChangedHandler = DispatchService.GuiDispatch<EventHandler> (OnBreakpointChanged);
			
			breakpoints.BreakpointAdded += breakpointAddedHandler;
			breakpoints.BreakpointRemoved += breakpointRemovedHandler;
			breakpoints.Changed += breakpointChangedHandler;
			breakpoints.BreakpointUpdated += breakpointUpdatedHandler;
			
			DebuggingService.PausedEvent += OnDebuggerStatusCheck;
			DebuggingService.ResumedEvent += OnDebuggerStatusCheck;
			DebuggingService.StoppedEvent += OnDebuggerStatusCheck;
			
			tree.RowActivated += OnRowActivated;
			
			DockItemToolbar toolbar = window.GetToolbar (PositionType.Top);
			toolbar.Add (toolbarSet, sw);
			toolbar.ShowAll ();
		}
		
		public void Dispose ()
		{
			breakpoints.BreakpointAdded -= breakpointAddedHandler;
			breakpoints.BreakpointRemoved -= breakpointRemovedHandler;
			breakpoints.Changed -= breakpointChangedHandler;
			breakpoints.BreakpointUpdated -= breakpointUpdatedHandler;
			
			DebuggingService.PausedEvent -= OnDebuggerStatusCheck;
			DebuggingService.ResumedEvent -= OnDebuggerStatusCheck;
			DebuggingService.StoppedEvent -= OnDebuggerStatusCheck;
		}

		void ShowPopup (Gdk.EventButton evt)
		{
			IdeApp.CommandService.ShowContextMenu (tree, evt, menuSet, tree);
		}
		
		[CommandHandler (LocalCommands.Properties)]
		protected void OnProperties ()
		{
			var selected = tree.Selection.GetSelectedRows ();
			TreeIter iter;

			if (selected.Length == 1 && store.GetIter (out iter, selected[0])) {
				BreakEvent bp = (BreakEvent) store.GetValue (iter, (int) Columns.Breakpoint);
				if (DebuggingService.ShowBreakpointProperties (ref bp))
					UpdateDisplay ();
			}
		}

		string GetIconId (BreakEvent bp)
		{
			if (bp is Catchpoint) {
				return bp.Enabled ? "md-catchpoint" : "md-catchpoint-disabled";
			} else {
				return bp.Enabled ? "md-breakpoint" : "md-breakpoint-disabled";
			}
		}

		
		[CommandHandler (DebugCommands.EnableDisableBreakpoint)]
		protected void OnEnableDisable ()
		{
			breakpoints.Changed -= breakpointChangedHandler;

			try {
				bool enable = false;

				// If any breakpoints are disabled, we'll enable them all. Otherwise, disable them all.
				foreach (var path in tree.Selection.GetSelectedRows ()) {
					TreeIter iter;

					if (!store.GetIter (out iter, path))
						continue;

					BreakEvent bp = (BreakEvent) store.GetValue (iter, (int) Columns.Breakpoint);
					if (!bp.Enabled) {
						enable = true;
						break;
					}
				}

				foreach (var path in tree.Selection.GetSelectedRows ()) {
					TreeIter iter;

					if (!store.GetIter (out iter, path))
						continue;

					BreakEvent bp = (BreakEvent) store.GetValue (iter, (int) Columns.Breakpoint);
					bp.Enabled = enable;

					store.SetValue (iter, (int) Columns.Icon, GetIconId(bp));
					store.SetValue (iter, (int) Columns.Selected, enable);
				}
			} finally {
				breakpoints.Changed += breakpointChangedHandler;
			}
		}
		
		[CommandHandler (LocalCommands.GoToFile)]
		protected void OnBpJumpTo ()
		{
			var selected = tree.Selection.GetSelectedRows ();
			TreeIter iter;

			if (selected.Length == 1 && store.GetIter (out iter, selected[0])) {
				var be = (BreakEvent) store.GetValue (iter, (int) Columns.Breakpoint);
				var bp = be as Breakpoint;
				if (bp != null) {
					if (!string.IsNullOrEmpty (bp.FileName))
						IdeApp.Workbench.OpenDocument (bp.FileName, bp.Line, 1);
				}
			}
		}

		bool DeleteSelectedBreakpoints ()
		{
			bool deleted = false;

			breakpoints.BreakpointRemoved -= breakpointRemovedHandler;

			try {
				// Note: since we'll be modifying the list of breakpoints, we need to sort
				// the paths in reverse order.
				var selected = tree.Selection.GetSelectedRows ();
				Array.Sort (selected, new TreePathComparer (true));

				foreach (var path in selected) {
					TreeIter iter;

					if (!store.GetIter (out iter, path))
						continue;

					var bp = (BreakEvent) store.GetValue (iter, (int) Columns.Breakpoint);
					lock (breakpoints)
						breakpoints.Remove (bp);
					deleted = true;
				}
			} finally {
				breakpoints.BreakpointRemoved += breakpointRemovedHandler;
			}

			return deleted;
		}

		[CommandUpdateHandler (EditCommands.SelectAll)]
		protected void UpdateSelectAll (CommandInfo cmd)
		{
			TreeIter iter;

			cmd.Enabled = store.GetIterFirst (out iter);
		}

		[CommandHandler (EditCommands.SelectAll)]
		protected void OnSelectAll ()
		{
			tree.Selection.SelectAll ();
		}

		[CommandHandler (EditCommands.Delete)]
		[CommandHandler (EditCommands.DeleteKey)]
		protected void OnDeleted ()
		{
			if (DeleteSelectedBreakpoints ())
				UpdateDisplay ();
		}

		[CommandUpdateHandler (LocalCommands.GoToFile)]
		[CommandUpdateHandler (LocalCommands.Properties)]
		protected void UpdateBpCommand (CommandInfo cmd)
		{
			cmd.Enabled = tree.Selection.CountSelectedRows () == 1;
		}
		
		[CommandUpdateHandler (EditCommands.Delete)]
		[CommandUpdateHandler (EditCommands.DeleteKey)]
		[CommandUpdateHandler (DebugCommands.EnableDisableBreakpoint)]
		protected void UpdateMultiBpCommand (CommandInfo cmd)
		{
			cmd.Enabled = tree.Selection.CountSelectedRows () > 0;
		}

		[GLib.ConnectBefore]
		void OnKeyPressEvent (object sender, KeyPressEventArgs args)
		{
			// Delete the currently selected breakpoint(s) with any delete key
			switch (args.Event.Key) {
			case Gdk.Key.Delete:
			case Gdk.Key.KP_Delete:
			case Gdk.Key.BackSpace:
				if (DeleteSelectedBreakpoints ()) {
					args.RetVal = true;
					UpdateDisplay ();
				}
				break;
			case Gdk.Key.space:
				if (tree.Selection.CountSelectedRows () > 0) {
					OnEnableDisable ();
					args.RetVal = true;
				}
				break;
			}
		}
		
		void ItemToggled (object o, ToggledArgs args)
		{
			breakpoints.Changed -= breakpointChangedHandler;
			
			try {
				TreeIter iter;

				if (store.GetIterFromString (out iter, args.Path)) {
					BreakEvent bp = (BreakEvent) store.GetValue (iter, (int) Columns.Breakpoint);
					bp.Enabled = !bp.Enabled;

					store.SetValue (iter, (int) Columns.Icon, GetIconId(bp));
					store.SetValue (iter, (int) Columns.Selected, bp.Enabled);
				}
			} finally {
				breakpoints.Changed += breakpointChangedHandler;
			}
		}
		
		public void UpdateDisplay ()
		{
			if (tree.IsRealized)
				tree.ScrollToPoint (0, 0);

			treeState.Save ();
			
			store.Clear ();
			if (breakpoints != null) {	
				lock (breakpoints) {
					foreach (BreakEvent be in breakpoints.GetBreakevents ()) {
						string hitCount = be.HitCountMode != HitCountMode.None ? be.CurrentHitCount.ToString () : "";
						string traceExp = (be.HitAction & HitAction.PrintExpression) != HitAction.None ? be.TraceExpression : "";
						string traceVal = (be.HitAction & HitAction.PrintExpression) != HitAction.None ? be.LastTraceValue : "";
						string name;

						var fb = be as FunctionBreakpoint;
						var bp = be as Breakpoint;
						var cp = be as Catchpoint;
						if (fb != null) {
							if (fb.ParamTypes != null)
								name = fb.FunctionName + "(" + string.Join (", ", fb.ParamTypes) + ")";
							else
								name = fb.FunctionName;
						} else if (bp != null) {
							name = String.Format ("{0}:{1},{2}", bp.FileName, bp.Line, bp.Column);
						} else if (cp != null) {
							name = cp.ExceptionName;
						} else {
							name = "";
						}

						store.AppendValues (GetIconId (be), be.Enabled, name, be, bp != null ? bp.ConditionExpression : null, traceExp, hitCount, traceVal);
					}
				}
			}

			treeState.Load ();
		}
		
		void OnBreakpointUpdated (object s, BreakpointEventArgs args)
		{
			TreeIter it;

			if (!store.GetIterFirst (out it))
				return;

			do {
				var bp = (BreakEvent) store.GetValue (it, (int) Columns.Breakpoint);
				if (bp == args.Breakpoint) {
					string hitCount = bp.HitCountMode != HitCountMode.None ? bp.CurrentHitCount.ToString () : "";
					string traceVal = (bp.HitAction & HitAction.PrintExpression) != HitAction.None ? bp.LastTraceValue : "";
					store.SetValue (it, (int) Columns.HitCount, hitCount);
					store.SetValue (it, (int) Columns.LastTrace, traceVal);
					break;
				}
			} while (store.IterNext (ref it));
		}
		
		protected void OnBreakpointAdded (object o, EventArgs args)
		{
			UpdateDisplay ();	
		}
		
		protected void OnBreakpointRemoved (object o, EventArgs args)
		{
			UpdateDisplay ();	
		}
		
		protected void OnBreakpointChanged (object o, EventArgs args)
		{
			UpdateDisplay ();	
		}
		
		void OnDebuggerStatusCheck (object s, EventArgs a)
		{
			if (control != null)
				control.Sensitive = !breakpoints.IsReadOnly;
		}

		void OnRowActivated (object o, Gtk.RowActivatedArgs args)
		{
			OnBpJumpTo ();
		}
		
		public Gtk.Widget Control {
			get {
				return control;
			}
		}

		public string Id {
			get { return "MonoDevelop.Debugger.BreakpointPad"; }
		}

		public string DefaultPlacement {
			get { return "Bottom"; }
		}

		public void RedrawContent ()
		{
			UpdateDisplay ();
		}
		
		protected void OnDeleteClicked (object o, EventArgs args)
		{
			OnDeleted ();
		}
	}
}