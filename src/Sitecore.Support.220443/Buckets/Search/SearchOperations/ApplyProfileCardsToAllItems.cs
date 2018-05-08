// --------------------------------------------------------------------------------------------------------------------
// <copyright file="ApplyProfileCardsToAllItems.cs" company="Sitecore">
//   Copyright (c) Sitecore. All rights reserved.
// </copyright>
// <summary>
//   The apply profile cards to all items.
// </summary>
// --------------------------------------------------------------------------------------------------------------------

using System;
using System.Linq;

using Sitecore.Buckets.Commands;
using Sitecore.ContentSearch.SearchTypes;
using Sitecore.Globalization;
using Sitecore.Security.Accounts;
using Sitecore.SecurityModel;
using Sitecore.StringExtensions;

namespace Sitecore.Support.Buckets.Search.SearchOperations
{
  using System.Collections.Generic;
  using System.Collections.Specialized;

  using Configuration;
  using ContentSearch;
  using ContentSearch.Utilities;
  using Data.Items;
  using Diagnostics;
  using Shell.Applications.Dialogs.ProgressBoxes;
  using Shell.Framework.Commands;
  using Text;
  using Web;
  using Web.UI.Sheer;
  using Web.UI.WebControls;
  using Web.UI.XamlSharp.Continuations;

  /// <summary>The apply profile cards to all items.</summary>
  [Serializable]
  internal class ApplyProfileCardsToAllItems : Command, ISupportsContinuation, IItemBucketsCommand
  {
    // Methods

    /// <summary>The execute.</summary>
    /// <param name="context">The context.</param>
    public override void Execute(CommandContext context)
    {
      Assert.ArgumentNotNull(context, "context");
      if (context.Items.Length == 1)
      {
        var item = context.Items[0];

        if (this.QueryState(context) != CommandState.Enabled)
        {
          SheerResponse.Alert(Sitecore.Buckets.Localization.Texts.NoRights);
          return;
        }

        if (!item.Appearance.ReadOnly && item.Access.CanWrite())
        {
          var parameters = new NameValueCollection();
          parameters["id"] = item.ID.ToString();
          parameters["language"] = (Context.Language == null) ? item.Language.ToString() : Context.Language.ToString();
          parameters["version"] = item.Version.ToString();
          parameters["database"] = item.Database.Name;
          parameters["isPageEditor"] = (context.Parameters["pageEditor"] == "1") ? "1" : "0";
          parameters["searchString"] = context.Parameters.GetValues("url")[0].Replace("\"", string.Empty);

          if (ContinuationManager.Current != null)
          {
            ContinuationManager.Current.Start(this, "Run", new ClientPipelineArgs(parameters));
          }
          else
          {
            Context.ClientPage.Start(this, "Run", parameters);
          }
        }
      }
    }

    /// <summary>The get name.</summary>
    /// <returns>The System.String.</returns>
    protected virtual string GetName()
    {
      return Translate.Text(Sitecore.Buckets.Localization.Texts.CreatingSnapshotPoint);
    }

    /// <summary>The get url.</summary>
    /// <returns>The System.String.</returns>
    protected new virtual string GetUrl()
    {
      return "/sitecore/shell/~/xaml/Sitecore.Shell.Applications.Analytics.Personalization.ProfileCardsForm.aspx";
    }

    /// <summary>The run.</summary>
    /// <param name="args">The args.</param>
    protected void Run(ClientPipelineArgs args)
    {
      Assert.ArgumentNotNull(args, "args");
      if (SheerResponse.CheckModified())
      {
        if (args.IsPostBack)
        {
          if (((Context.Page != null) && (Context.Page.Page != null)) && ((Context.Page.Page.Session["TrackingFieldModified"] as string) == "1"))
          {
            Context.Page.Page.Session["TrackingFieldModified"] = null;
            if (args.Parameters["isPageEditor"] == "1")
            {
              Reload(new UrlString(WebUtil.GetRequestUri404(WebUtil.GetQueryString("url"))));
            }
            else if (AjaxScriptManager.Current != null)
            {
              AjaxScriptManager.Current.Dispatch("analytics:trackingchanged");
            }
            else
            {
              // The dialog - ProfileCardsForm - doesn't return any results, it rather commits
              // changes right away - no OK/Cancel, only Close button
              // the condition above - (Context.Page.Page.Session["TrackingFieldModified"]  == 1 does the job
              Context.ClientPage.SendMessage(this, "analytics:trackingchanged");
              var tempItem = Factory.GetDatabase(args.Parameters["database"]).GetItem(args.Parameters["id"]);
              var tempTrack = Context.ClientData.GetValue("tempTrackingField").ToString();
              var valueForAllOtherItems = tempItem.Fields[Sitecore.Buckets.Util.Constants.PersonalisationField].Value;
              var searchStringModel = SearchStringModel.ExtractSearchQuery(args.Parameters["searchString"]);
              var jobName = Translate.Text(Sitecore.Buckets.Localization.Texts.BulkApplyingProfileCards);
              var parameters = new object[]
              {
                                tempItem, searchStringModel, valueForAllOtherItems, args.Parameters["searchString"], args.Parameters["database"], tempTrack, Context.User
              };
              ProgressBox.Execute(jobName, this.GetName(), "Business/16x16/radar-chart.png", this.StartProcess, parameters);
              SheerResponse.Alert(Translate.Text(Sitecore.Buckets.Localization.Texts.FinishedApplyingProfileScores));
            }
          }
        }
        else
        {
          var tempItem = Factory.GetDatabase(args.Parameters["database"]).GetItem(args.Parameters["id"]);
          Context.ClientData.SetValue("tempTrackingField", tempItem.Fields[Sitecore.Buckets.Util.Constants.PersonalisationField].Value);
          var urlString = new UrlString("/sitecore/shell/~/xaml/Sitecore.Shell.Applications.Analytics.Personalization.ProfileCardsForm.aspx");
          var handle = new UrlHandle();
          handle["itemid"] = args.Parameters["id"];
          handle["databasename"] = args.Parameters["database"];
          handle["la"] = args.Parameters["language"];
          handle.Add(urlString);
          SheerResponse.ShowModalDialog(urlString.ToString(), "1000", "600", string.Empty, true);
          args.WaitForPostBack();
        }
      }
    }

    /// <summary>Reloads the specified URL.</summary>
    /// <param name="url">The URL.</param>
    protected static void Reload([NotNull] UrlString url)
    {
      Assert.ArgumentNotNull(url, "url");

      SheerResponse.Eval(
        @" try {{
        window.parent.location.href='{0}';
        }}
        catch(e) {{
          //silent because of IE issue #317501
        }};"
          .FormatWith(url.GetUrl()));
    }

    /// <summary>The show dialog.</summary>
    /// <param name="url">The url.</param>
    protected virtual void ShowDialog(string url)
    {
      Assert.ArgumentNotNull(url, "url");
      SheerResponse.ShowModalDialog(url, true);
    }

    /// <summary>Method for Creating Bucket</summary>
    /// <param name="parameters">The parameters.</param>
    private void StartProcess(params object[] parameters)
    {
      var item = (Item)parameters[0];
      var tempItem = (SitecoreIndexableItem)item;
      if (tempItem == null)
      {
        Log.Error("Apply Profile Cards - Unable to cast current item - " + parameters[0].GetType().FullName, this);
        return;
      }

      var searchStringModel = (List<SearchStringModel>)parameters[1];
      var valueForAllOtherItems = (string)parameters[2];
      var searchString = (string)parameters[3];
      var dataBase = (string)parameters[4];
      var tempTrack = (string)parameters[5];
      var account = (User)parameters[6];

      item.Editing.BeginEdit();
      item[Sitecore.Buckets.Util.Constants.PersonalisationField] = tempTrack;
      item.Editing.EndEdit();

      using (var searchContext = ContentSearchManager.GetIndex(tempItem).CreateSearchContext())
      {
        #region Patch 220443

        var listOfItems = LinqHelper.CreateQuery<SitecoreUISearchResultItem>(
          searchContext,
          searchStringModel,
          tempItem).Where(i => i.Paths.Contains(item.ID));
        
        #endregion

        Assert.IsNotNull(tempItem, "item");

        foreach (var item1 in listOfItems.Select(sitecoreItem => sitecoreItem.GetItem()))
        {
          using (new SecurityEnabler())
          {
            if (item1 == null || !item1.Security.CanWrite(account))
            {
              continue;
            }
          }

          Context.Job.Status.Messages.Add(Translate.Text(Sitecore.Buckets.Localization.Texts.ApplyingProfileCardToItem, item1.Paths.FullPath));

          item1.Editing.BeginEdit();
          item1[Sitecore.Buckets.Util.Constants.PersonalisationField] = valueForAllOtherItems;
          item1.Editing.EndEdit();
        }
      }
    }
  }
}