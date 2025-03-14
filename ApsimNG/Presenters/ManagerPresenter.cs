﻿// -----------------------------------------------------------------------
// <copyright file="ManagerPresenter.cs"  company="APSIM Initiative">
//     Copyright (c) APSIM Initiative
// </copyright>
// -----------------------------------------------------------------------

namespace UserInterface.Presenters
{
    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using System.Linq;
    using System.Reflection;
    using APSIM.Shared.Utilities;
    using EventArguments;
    using Models;
    using Models.Core;
    using Views;
    using System.IO;
    using System.Diagnostics;
    using System.Threading.Tasks;
    using ICSharpCode.NRefactory.CSharp;

    /// <summary>
    /// Presenter for the Manager component
    /// </summary>
    public class ManagerPresenter : IPresenter
    {
        /// <summary>
        /// The presenter used for properties
        /// </summary>
        private PropertyPresenter propertyPresenter = new PropertyPresenter();

        /// <summary>
        /// The manager object
        /// </summary>
        private Manager manager;

        /// <summary>
        /// The compiled script model.
        /// </summary>
        private Model scriptModel;

        /// <summary>
        /// The view for the manager
        /// </summary>
        private IManagerView managerView;

        /// <summary>
        /// The explorer presenter used
        /// </summary>
        private ExplorerPresenter explorerPresenter;

        /// <summary>
        /// Handles generation of completion options for the view.
        /// </summary>
        private IntellisensePresenter intellisense;

        /// <summary>
        /// Attach the Manager model and ManagerView to this presenter.
        /// </summary>
        /// <param name="model">The model</param>
        /// <param name="view">The view to attach</param>
        /// <param name="presenter">The explorer presenter being used</param>
        public void Attach(object model, object view, ExplorerPresenter presenter)
        {
            manager = model as Manager;
            managerView = view as IManagerView;
            explorerPresenter = presenter;
            intellisense = new IntellisensePresenter(managerView as ViewBase);
            intellisense.ItemSelected += OnIntellisenseItemSelected;

            scriptModel = manager.Children.FirstOrDefault();
            propertyPresenter.Attach(scriptModel, managerView.GridView, presenter);
            managerView.Editor.Mode = EditorType.ManagerScript;
            managerView.Editor.Text = manager.Code;
            managerView.Editor.ContextItemsNeeded += OnNeedVariableNames;
            managerView.Editor.LeaveEditor += OnEditorLeave;
            managerView.Editor.AddContextSeparator();
            managerView.Editor.AddContextActionWithAccel("Test compile", OnDoCompile, "Ctrl+T");
            managerView.Editor.AddContextActionWithAccel("Reformat", OnDoReformat, "Ctrl+R");
            managerView.Editor.Location = manager.Location;
            managerView.TabIndex = manager.ActiveTabIndex;
            presenter.CommandHistory.ModelChanged += CommandHistory_ModelChanged;
        }

        /// <summary>
        /// Detach the model from the view.
        /// </summary>
        public void Detach()
        {
            BuildScript();  // compiles and saves the script
            propertyPresenter.Detach();

            explorerPresenter.CommandHistory.ModelChanged -= CommandHistory_ModelChanged;
            managerView.Editor.ContextItemsNeeded -= OnNeedVariableNames;
            managerView.Editor.LeaveEditor -= OnEditorLeave;
            intellisense.ItemSelected -= OnIntellisenseItemSelected;
            intellisense.Cleanup();
        }

        /// <summary>
        /// The view is asking for variable names for its intellisense.
        /// </summary>
        /// <param name="sender">Sending object</param>
        /// <param name="e">Context item arguments</param>
        public void OnNeedVariableNames(object sender, NeedContextItemsArgs e)
        {
            try
            {
                if (e.ControlShiftSpace)
                    intellisense.ShowScriptMethodCompletion(manager, e.Code, e.Offset, new Point(e.Coordinates.X, e.Coordinates.Y));
                else if (intellisense.GenerateScriptCompletions(e.Code, e.Offset, e.ControlSpace))
                    intellisense.Show(e.Coordinates.X, e.Coordinates.Y);
            }
            catch (Exception err)
            {
                explorerPresenter.MainPresenter.ShowError(err);
            }
        }

        /// <summary>
        /// The user has changed the code script.
        /// </summary>
        /// <param name="sender">Sending object</param>
        /// <param name="e">The arguments</param>
        public void OnEditorLeave(object sender, EventArgs e)
        {
            // explorerPresenter.CommandHistory.ModelChanged += CommandHistory_ModelChanged;
            if (!intellisense.Visible)
                BuildScript();
            if (scriptModel != null)
            {
                propertyPresenter.UpdateModel(scriptModel);
                propertyPresenter.Refresh();
            }
        }

        /// <summary>
        /// The model has changed so update our view.
        /// </summary>
        /// <param name="changedModel">The changed manager model</param>
        public void CommandHistory_ModelChanged(object changedModel)
        {
            if (changedModel == manager)
            {
                managerView.Editor.Text = manager.Code;
            }
            else if (changedModel == scriptModel)
            {
                propertyPresenter.UpdateModel(scriptModel);
            }
        }

        /// <summary>
        /// Build the script
        /// </summary>
        private void BuildScript()
        {
            explorerPresenter.CommandHistory.ModelChanged -= CommandHistory_ModelChanged;

            try
            {
                manager.Location = managerView.Editor.Location;
                manager.ActiveTabIndex = managerView.TabIndex;

                string code = managerView.Editor.Text;

                // set the code property manually first so that compile error can be trapped via
                // an exception.
                bool codeChanged = manager.Code != code;
                manager.Code = code;

                // If it gets this far then compiles ok.
                if (codeChanged)
                {
                    explorerPresenter.CommandHistory.Add(new Commands.ChangeProperty(manager, "Code", code));
                }

                explorerPresenter.MainPresenter.ShowMessage("\"" + manager.Name + "\" compiled successfully", Simulation.MessageType.Information);
            }
            catch (Exception err)
            {
                explorerPresenter.MainPresenter.ShowError(err);
            }

            try
            {
                // User could have added more inputs to manager script - therefore we update the property presenter.
                scriptModel = Apsim.Child(manager, "Script") as Model;
                if (scriptModel != null)
                    propertyPresenter.Refresh();
            }
            catch (Exception err)
            {
                explorerPresenter.MainPresenter.ShowError(err);
            }

            explorerPresenter.CommandHistory.ModelChanged += CommandHistory_ModelChanged;
        }

        /// <summary>
        /// Perform a compile
        /// </summary>
        /// <param name="sender">Sender object</param>
        /// <param name="e">Event arguments</param>
        private void OnDoCompile(object sender, EventArgs e)
        {
            BuildScript();
        }

        /// <summary>
        /// Perform a reformat of the text
        /// </summary>
        /// <param name="sender">Sender object</param>
        /// <param name="e">Event arguments</param>
        private void OnDoReformat(object sender, EventArgs e)
        {
            CSharpFormatter formatter = new CSharpFormatter(FormattingOptionsFactory.CreateAllman());
            string newText = formatter.Format(managerView.Editor.Text);
            managerView.Editor.Text = newText;
            explorerPresenter.CommandHistory.Add(new Commands.ChangeProperty(manager, "Code", newText));
        }

        /// <summary>
        /// Invoked when the user selects an item in the intellisense.
        /// Inserts the selected item at the caret.
        /// </summary>
        /// <param name="sender">Sender object.</param>
        /// <param name="args">Event arguments.</param>
        private void OnIntellisenseItemSelected(object sender, IntellisenseItemSelectedArgs args)
        {
            try
            {
                managerView.Editor.InsertCompletionOption(args.ItemSelected, args.TriggerWord);
                if (args.IsMethod)
                    intellisense.ShowScriptMethodCompletion(manager, managerView.Editor.Text, managerView.Editor.Offset, managerView.Editor.GetPositionOfCursor());
            }
            catch (Exception err)
            {
                explorerPresenter.MainPresenter.ShowError(err);
            }
        }
    }
}
