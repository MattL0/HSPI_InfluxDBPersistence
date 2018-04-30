﻿using HomeSeerAPI;
using InfluxData.Net.Common.Enums;
using InfluxData.Net.InfluxDb;
using NullGuard;
using Scheduler;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Text;
using System.Web;

namespace Hspi
{
    using static System.FormattableString;

    /// <summary>
    /// Helper class to generate configuration page for plugin
    /// </summary>
    /// <seealso cref="Scheduler.PageBuilderAndMenu.clsPageBuilder" />
    [NullGuard(ValidationFlags.Arguments | ValidationFlags.NonPublic)]
    internal class ConfigPage : PageBuilderAndMenu.clsPageBuilder
    {
        protected const string IdPrefix = "id_";

        /// <summary>
        /// Initializes a new instance of the <see cref="ConfigPage" /> class.
        /// </summary>
        /// <param name="HS">The hs.</param>
        /// <param name="pluginConfig">The plugin configuration.</param>
        public ConfigPage(IHSApplication HS, PluginConfig pluginConfig) : base(pageName)
        {
            this.HS = HS;
            this.pluginConfig = pluginConfig;
        }

        /// <summary>
        /// Gets the name of the web page.
        /// </summary>
        public static string Name => pageName;

        /// <summary>
        /// Get the web page string for the configuration page.
        /// </summary>
        /// <returns>
        /// System.String.
        /// </returns>
        public string GetWebPage(string queryString)
        {
            try
            {
                NameValueCollection parts = HttpUtility.ParseQueryString(queryString);

                string pageType = parts[PageTypeId];
                reset();

                AddHeader(HS.GetPageHeader(Name, "Configuration", string.Empty, string.Empty, false, false));

                StringBuilder stb = new StringBuilder();
                stb.Append(PageBuilderAndMenu.clsPageBuilder.DivStart("pluginpage", string.Empty));

                switch (pageType)
                {
                    case EditDevicePageType:
                        pluginConfig.DevicePersistenceData.TryGetValue(parts[PersistenceId], out var data);
                        stb.Append(BuildAddNewWebPageBody(data));
                        break;

                    default:
                    case null:
                        {
                            string tab = parts[TabId] ?? "0";
                            int tabId = 0;
                            int.TryParse(tab, out tabId);
                            stb.Append(BuildWebPageBody(tabId));
                            break;
                        }
                }

                AddBody(stb.ToString());
                stb.Append(PageBuilderAndMenu.clsPageBuilder.DivEnd());

                AddFooter(HS.GetPageFooter());
                suppressDefaultFooter = true;

                return BuildPage();
            }
            catch (Exception)
            {
                return "error";
            }
        }

        /// <summary>
        /// The user has selected a control on the configuration web page.
        /// The post data is provided to determine the control that initiated the post and the state of the other controls.
        /// </summary>
        /// <param name="data">The post data.</param>
        /// <param name="user">The name of logged in user.</param>
        /// <param name="userRights">The rights of the logged in user.</param>
        /// <returns>Any serialized data that needs to be passed back to the web page, generated by the clsPageBuilder class.</returns>
        public string PostBackProc(string data, [AllowNull]string user, int userRights)
        {
            NameValueCollection parts = HttpUtility.ParseQueryString(data);

            string form = parts["id"];

            if (form == NameToIdWithPrefix(SettingSaveButtonName))
            {
                StringBuilder results = new StringBuilder();

                // Validate

                System.Uri dbUri;

                if (!System.Uri.TryCreate(parts[DBUriKey], UriKind.Absolute, out dbUri))
                {
                    results.AppendLine("Url is not Valid.<br>");
                }

                string database = parts[DBKey];

                if (string.IsNullOrWhiteSpace(database))
                {
                    results.AppendLine("Database is not Valid.<br>");
                }

                string username = parts[UserKey];
                string password = parts[PasswordKey];

                try
                {
                    var influxDbClient = new InfluxDbClient(dbUri.ToString(), username, password, InfluxDbVersion.v_1_3);

                    var databases = influxDbClient.Database.GetDatabasesAsync().Result;

                    if (!databases.Any((db) => { return db.Name == database; }))
                    {
                        results.AppendLine("Database not found on server.<br>");
                    }
                }
                catch (Exception ex)
                {
                    results.AppendLine(Invariant($"Failed to connect to InfluxDB with {ex.GetFullMessage()}"));
                }

                if (results.Length > 0)
                {
                    this.divToUpdate.Add(ErrorDivId, results.ToString());
                }
                else
                {
                    this.divToUpdate.Add(ErrorDivId, string.Empty);
                    var dbConfig = new InfluxDBLoginInformation(dbUri, username, password, parts[DBKey]);
                    this.pluginConfig.DBLoginInformation = dbConfig;
                    this.pluginConfig.DebugLogging = parts[DebugLoggingId] == "checked";
                    this.pluginConfig.FireConfigChanged();
                }
            }
            else if (form == NameToIdWithPrefix(EditPersistenceCancel))
            {
                this.divToUpdate.Add(SaveErrorDivId, RedirectPage(Invariant($"/{HttpUtility.UrlEncode(ConfigPage.Name)}?{TabId}=1")));
            }
            else if (form == NameToIdWithPrefix(EditPersistenceSave))
            {
                StringBuilder results = new StringBuilder();

                string measurement = parts[MeasurementId];
                if (string.IsNullOrWhiteSpace(measurement))
                {
                    results.AppendLine("Measurement is not Valid.<br>");
                }

                string field = parts[FieldId];
                if (string.IsNullOrWhiteSpace(field))
                {
                    results.AppendLine("Field is not Valid.<br>");
                }

                string deviceId = parts[DeviceRefId];
                if (!int.TryParse(deviceId, out int deviceRefId))
                {
                    results.AppendLine("Device is not Valid.<br>");
                }

                string tagsString = parts[TagsId];
                var tagsList = tagsString.Split(new string[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);

                var tags = new Dictionary<string, string>();
                foreach (var tagString in tagsList)
                {
                    if (string.IsNullOrWhiteSpace(tagString))
                    {
                        continue;
                    }

                    var pair = tagString.Split('=');

                    if (pair.Length != 2)
                    {
                        results.AppendLine(Invariant($"Unknown tag type: {tagString}. Format tagType= value<br>"));
                    }
                    else
                    {
                        tags.Add(pair[0], pair[1]);
                    }
                }

                if (results.Length > 0)
                {
                    this.divToUpdate.Add(SaveErrorDivId, results.ToString());
                }
                else
                {
                    string persistenceId = parts[PersistenceId];

                    if (string.IsNullOrWhiteSpace(persistenceId))
                    {
                        persistenceId = System.Guid.NewGuid().ToString();
                    }

                    this.pluginConfig.AddDevicePersistenceData(new DevicePersistenceData(persistenceId, deviceRefId, measurement, field, tags));
                    this.pluginConfig.FireConfigChanged();
                    this.divToUpdate.Add(SaveErrorDivId, RedirectPage(Invariant($"/{HttpUtility.UrlEncode(ConfigPage.Name)}?{TabId}=1")));
                }
            }
            else if (form == NameToIdWithPrefix(DeletePersistenceSave))
            {
                this.pluginConfig.RemoveDevicePersistenceData(parts[PersistenceId]);
                this.pluginConfig.FireConfigChanged();
                this.divToUpdate.Add(SaveErrorDivId, RedirectPage(Invariant($"/{HttpUtility.UrlEncode(ConfigPage.Name)}?{TabId}=1")));
            }

            return base.postBackProc(Name, data, user, userRights);
        }

        /// <summary>
        /// Builds the web page body for the configuration page.
        /// The page has separate forms so that only the data in the appropriate form is returned when a button is pressed.
        /// </summary>
        private string BuildWebPageBody(int defaultTab)
        {
            int i = 0;
            StringBuilder stb = new StringBuilder();

            var tabs = new clsJQuery.jqTabs("tab1id", PageName);
            var tab1 = new clsJQuery.Tab();
            tab1.tabTitle = "DB Settings";
            tab1.tabDIVID = Invariant($"tabs{i++}");
            tab1.tabContent = BuildDBSettingTab();
            tabs.tabs.Add(tab1);

            var tab2 = new clsJQuery.Tab();
            tab2.tabTitle = "Persistence";
            tab2.tabDIVID = Invariant($"tabs{i++}");
            tab2.tabContent = BuildPersistenceTab();
            tabs.tabs.Add(tab2);

            switch (defaultTab)
            {
                case 0:
                    tabs.defaultTab = tab1.tabDIVID;
                    break;

                case 1:
                    tabs.defaultTab = tab2.tabDIVID;
                    break;
            }

            tabs.postOnTabClick = false;
            stb.Append(tabs.Build());

            return stb.ToString();
        }

        private string BuildDBSettingTab()
        {
            var dbConfig = pluginConfig.DBLoginInformation;

            StringBuilder stb = new StringBuilder();
            stb.Append(PageBuilderAndMenu.clsPageBuilder.FormStart("ftmSettings", "IdSettings", "Post"));

            stb.Append(@"<br>");
            stb.Append(@"<div>");
            stb.Append(@"<table class='full_width_table'");
            stb.Append("<tr height='5'><td style='width:25%'></td><td style='width:75%'></td></tr>");
            stb.Append(Invariant($"<tr><td class='tablecell'>Url:</td><td class='tablecell' style='width: 50px'>{HtmlTextBox(DBUriKey, dbConfig.DBUri != null ? dbConfig.DBUri.ToString() : string.Empty, type: "url")}</td></tr>"));
            stb.Append(Invariant($"<tr><td class='tablecell'>User:</td><td class='tablecell'>{HtmlTextBox(UserKey, dbConfig.User)} </td></tr>"));
            stb.Append(Invariant($"<tr><td class='tablecell'>Password:</td><td class='tablecell'>{HtmlTextBox(PasswordKey, dbConfig.Password)}</td></tr>"));
            stb.Append(Invariant($"<tr><td class='tablecell'>Database:</td><td colspan=2 class='tablecell'>{HtmlTextBox(DBKey, dbConfig.DB)}</ td ></tr>"));
            stb.Append(Invariant($"<tr><td class='tablecell'>Debug Logging Enabled:</td><td class='tablecell'>{FormCheckBox(DebugLoggingId, string.Empty, this.pluginConfig.DebugLogging)}</td ></tr>"));
            stb.Append(Invariant($"<tr><td colspan=2><div id='{ErrorDivId}' style='color:Red'></div></td></tr>"));
            stb.Append(Invariant($"<tr><td colspan=2>{FormButton(SettingSaveButtonName, "Save", "Save Settings")}</td></tr>"));
            stb.Append("<tr height='5'><td colspan=2></td></tr>");
            stb.Append(@" </table>");
            stb.Append(@"</div>");
            stb.Append(PageBuilderAndMenu.clsPageBuilder.FormEnd());

            return stb.ToString();
        }

        private string BuildPersistenceTab()
        {
            HSHelper hsHelper = new HSHelper(HS);
            StringBuilder stb = new StringBuilder();

            stb.Append(@"<div>");
            stb.Append(@"<table class='full_width_table'");
            stb.Append("<tr height='5'><td colspan=4></td></tr>");
            stb.Append(@"<tr>" +
                        "<td class='tablecolumn'>Device</td>" +
                        "<td class='tablecolumn'>Measurement</td>" +
                        "<td class='tablecolumn'>Field</td>" +
                        "<td class='tablecolumn'>Tags</td>" +
                        "<td class='tablecolumn'></td></tr>");

            foreach (var device in pluginConfig.DevicePersistenceData)
            {
                stb.Append(@"<tr>");
                string name = hsHelper.GetName(device.Value.DeviceRefId) ?? Invariant($"Unknown(RefId:{device.Value.DeviceRefId})");
                stb.Append(Invariant($"<td class='tablecell'><a href='/deviceutility?ref={device.Value.DeviceRefId}&edit=1'>{name}</a></td>"));
                stb.Append(Invariant($"<td class='tablecell'>{device.Value.Measurement}</td>"));
                stb.Append(Invariant($"<td class='tablecell'>{device.Value.Field}</td>"));
                stb.Append(@"<td class='tablecell'>");
                if (device.Value.Tags != null)
                {
                    foreach (var item in device.Value.Tags)
                    {
                        stb.Append(Invariant($"{item.Key}={item.Value}<br>"));
                    }
                }
                stb.Append("</td>");
                stb.Append(Invariant($"<td class='tablecell'>{PageTypeButton(Invariant($"Edit{device.Key}"), "Edit", EditDevicePageType, persistenceId: device.Key)}</ td ></ tr > "));
            }

            stb.Append(Invariant($"<tr><td colspan=5>{PageTypeButton("Add New Device", AddNewName, EditDevicePageType)}</td><td></td></tr>"));
            stb.Append(PageBuilderAndMenu.clsPageBuilder.FormStart("ftmSettings", "Id", "Post"));
            stb.Append(PageBuilderAndMenu.clsPageBuilder.FormEnd());

            stb.Append(Invariant($"<tr><td colspan=5></td></tr>"));
            stb.Append(@"<tr height='5'><td colspan=5></td></tr>");
            stb.Append(@" </table>");
            stb.Append(@"</div>");

            return stb.ToString();
        }

        private string BuildAddNewWebPageBody([AllowNull]DevicePersistenceData data)
        {
            HSHelper hsHelper = new HSHelper(HS);
            NameValueCollection PersistanceNameCollection = new NameValueCollection();

            foreach (var device in hsHelper.GetDevices())
            {
                PersistanceNameCollection.Add(device.Value, device.Key.ToString());
            }

            int deviceRefId = data != null ? data.DeviceRefId : -1;
            string measurement = data != null ? data.Measurement : string.Empty;
            string field = data != null ? data.Field : string.Empty;
            string tags = string.Empty;
            string id = data != null ? data.Id : string.Empty;
            string buttonLabel = data != null ? "Save" : "Add";
            string header = data != null ? "Edit Persistence" : "Add New DB Persistence";

            if (data != null && data.Tags != null)
            {
                foreach (var tag in data.Tags)
                {
                    tags += Invariant($"{tag.Key}={tag.Value}{Environment.NewLine}");
                }
            }

            StringBuilder stb = new StringBuilder();

            stb.Append(PageBuilderAndMenu.clsPageBuilder.FormStart("ftmDeviceChange", "IdChange", "Post"));

            stb.Append(@"<div>");
            stb.Append(@"<table class='full_width_table'");
            stb.Append("<tr height='5'><td style='width:25%'></td><td style='width:20%'></td><td style='width:55%'></td></tr>");
            stb.Append(Invariant($"<tr><td class='tableheader' colspan=3>{header}</td></tr>"));
            stb.Append(Invariant($"<tr><td class='tablecell'>Name:</td><td class='tablecell' colspan=2>{FormDropDown(DeviceRefId, PersistanceNameCollection, deviceRefId, 250, string.Empty)}</td></tr>"));
            stb.Append(Invariant($"<tr><td class='tablecell'>Measurement:</td><td class='tablecell' colspan=2>{HtmlTextBox(MeasurementId, measurement)}</td></tr>"));
            stb.Append(Invariant($"<tr><td class='tablecell'>Field:</td><td class='tablecell' colspan=2>{HtmlTextBox(FieldId, field)}</td></tr>"));
            stb.Append(Invariant($"<tr><td class='tablecell'>Tags:</td><td class='tablecell' colspan=2><p><small>Name and locations are automatically added as tags.</small></p>{TextArea(TagsId, tags)}</td></tr>"));

            stb.Append(Invariant($"<tr><td colspan=3>{HtmlTextBox(PersistenceId, id, type: "hidden")}<div id='{SaveErrorDivId}' style='color:Red'></div></td><td></td></tr>"));
            stb.Append(Invariant($"<tr><td colspan=3>{FormPageButton(EditPersistenceSave, buttonLabel)}"));

            if (data != null)
            {
                stb.Append(FormPageButton(DeletePersistenceSave, "Delete"));
            }

            stb.Append(FormPageButton(EditPersistenceCancel, "Cancel"));
            stb.Append(Invariant($"</td><td></td></tr>"));
            stb.Append("<tr height='5'><td colspan=3></td></tr>");
            stb.Append(@" </table>");
            stb.Append(@"</div>");
            stb.Append(PageBuilderAndMenu.clsPageBuilder.FormEnd());

            return stb.ToString();
        }

        private static string NameToId(string name)
        {
            return name.Replace(' ', '_');
        }

        private static string NameToIdWithPrefix(string name)
        {
            return Invariant($"{ IdPrefix}{NameToId(name)}");
        }

        protected static string HtmlTextBox(string name, string defaultText, int size = 25, string type = "text", bool @readonly = false)
        {
            return Invariant($"<input type=\'{type}\' id=\'{NameToIdWithPrefix(name)}\' size=\'{size}\' name=\'{name}\' value=\'{defaultText}\' {(@readonly ? "readonly" : string.Empty)}>");
        }

        protected static string TextArea(string name, string defaultText, int rows = 6, int cols = 35, bool @readonly = false)
        {
            return Invariant($"<textarea form_id=\'{NameToIdWithPrefix(name)}\' rows=\'{rows}\' col=\'{cols}\' name=\'{name}\'  {(@readonly ? "readonly" : string.Empty)}>{defaultText}</textarea>");
        }

        protected string FormDropDown(string name, NameValueCollection options, int selected, int width, string tooltip)
        {
            var dropdown = new clsJQuery.jqDropList(name, PageName, false)
            {
                selectedItemIndex = -1,
                id = NameToIdWithPrefix(name),
                autoPostBack = false,
                toolTip = tooltip,
                style = Invariant($"width: {width}px;"),
                enabled = true
            };

            if (options != null)
            {
                for (var i = 0; i < options.Count; i++)
                {
                    var sel = i == selected;
                    dropdown.AddItem(options.GetKey(i), options.Get(i), sel);
                }
            }

            return dropdown.Build();
        }

        protected string FormButton(string name, string label, string toolTip)
        {
            var button = new clsJQuery.jqButton(name, label, PageName, true)
            {
                id = NameToIdWithPrefix(name),
                toolTip = toolTip,
            };
            button.toolTip = toolTip;
            button.enabled = true;

            return button.Build();
        }

        protected string FormCheckBox(string name, string label, bool @checked, bool autoPostBack = false)
        {
            var cb = new clsJQuery.jqCheckBox(name, label, PageName, true, true)
            {
                id = NameToIdWithPrefix(name),
                @checked = @checked,
                autoPostBack = autoPostBack,
            };
            return cb.Build();
        }

        protected string PageTypeButton(string name, string label, string type, string persistenceId = null)
        {
            var b = new clsJQuery.jqButton(name, label, PageName, false)
            {
                id = NameToIdWithPrefix(name),
                url = Invariant($"/{HttpUtility.UrlEncode(ConfigPage.Name)}?{PageTypeId}={HttpUtility.UrlEncode(type)}&{PersistenceId}={HttpUtility.UrlEncode(persistenceId ?? string.Empty)}"),
            };

            return b.Build();
        }

        protected string FormPageButton(string name, string label)
        {
            var b = new clsJQuery.jqButton(name, label, PageName, true)
            {
                id = NameToIdWithPrefix(name),
            };

            return b.Build();
        }

        private const string SettingSaveButtonName = "SettingSave";
        private const string DebugLoggingId = "DebugLoggingId";
        private const string DBUriKey = "DBUriId";
        private const string PasswordKey = "PasswordId";
        private const string PageTypeId = "type";
        private const string AddNewName = "Add New";
        private const string EditPersistenceCancel = "CancelP";
        private const string EditPersistenceSave = "SaveP";
        private const string DeletePersistenceSave = "DeleteP";
        private const string PersistenceId = "PersistenceId";
        private const string EditDevicePageType = "edit";
        private const string ErrorDivId = "message_id";
        private const string ImageDivId = "image_id";
        private const string UserKey = "UserId";
        private const string DBKey = "DBId";
        private const string DeviceRefId = "DeviceRefId";
        private const string MeasurementId = "MeasurementId";
        private const string FieldId = "FieldId";
        private const string TagsId = "TagsId";
        private const string SaveErrorDivId = "SaveErrorDivId";
        private static readonly string pageName = Invariant($"{PlugInData.PlugInName} Configuration").Replace(' ', '_');
        private readonly IHSApplication HS;
        private readonly PluginConfig pluginConfig;
        private string TabId = "TabId";
    }
}