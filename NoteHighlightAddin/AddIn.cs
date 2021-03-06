﻿/*
 *  Copyright (c) Microsoft. All rights reserved. Licensed under the MIT license.
 */

using System;
using System.Diagnostics.CodeAnalysis;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Linq;
using Extensibility;
using Microsoft.Office.Core;
using NoteHighlightAddin.Utilities;
using Application = Microsoft.Office.Interop.OneNote.Application;  // Conflicts with System.Windows.Forms
using System.Reflection;
using System.Drawing;
using Microsoft.Office.Interop.OneNote;
using NoteHighLightForm;
using System.Text;
using System.Linq;
using Helper;
using System.Threading;
using System.Web;

#pragma warning disable CS3003 // Type is not CLS-compliant

namespace NoteHighlightAddin
{
	[ComVisible(true)]
	[Guid("4C6B0362-F139-417F-9661-3663C268B9E9"), ProgId("NoteHighlight2016.AddIn")]

	public class AddIn : IDTExtensibility2, IRibbonExtensibility
	{
		protected Application OneNoteApplication
		{ get; set; }

        private XNamespace ns;

        private MainForm mainForm;

        string tag;

		public AddIn()
		{
		}

		/// <summary>
		/// Returns the XML in Ribbon.xml so OneNote knows how to render our ribbon
		/// </summary>
		/// <param name="RibbonID"></param>
		/// <returns></returns>
		public string GetCustomUI(string RibbonID)
		{
			return Properties.Resources.ribbon;
		}

		public void OnAddInsUpdate(ref Array custom)
		{
		}

		/// <summary>
		/// Cleanup
		/// </summary>
		/// <param name="custom"></param>
		public void OnBeginShutdown(ref Array custom)
		{
			this.mainForm?.Invoke(new Action(() =>
			{
				// close the form on the forms thread
				this.mainForm?.Close();
				this.mainForm = null;
			}));
		}

		/// <summary>
		/// Called upon startup.
		/// Keeps a reference to the current OneNote application object.
		/// </summary>
		/// <param name="application"></param>
		/// <param name="connectMode"></param>
		/// <param name="addInInst"></param>
		/// <param name="custom"></param>
		public void OnConnection(object Application, ext_ConnectMode ConnectMode, object AddInInst, ref Array custom)
		{
			SetOneNoteApplication((Application)Application);
		}

		public void SetOneNoteApplication(Application application)
		{
			OneNoteApplication = application;
		}

		/// <summary>
		/// Cleanup
		/// </summary>
		/// <param name="RemoveMode"></param>
		/// <param name="custom"></param>
		[SuppressMessage("Microsoft.Reliability", "CA2001:AvoidCallingProblematicMethods", MessageId = "System.GC.Collect")]
		public void OnDisconnection(ext_DisconnectMode RemoveMode, ref Array custom)
		{
			OneNoteApplication = null;
			GC.Collect();
			GC.WaitForPendingFinalizers();
		}

		public void OnStartupComplete(ref Array custom)
		{
		}

		//public async Task AddInButtonClicked(IRibbonControl control)
        public void AddInButtonClicked(IRibbonControl control)
        {
            tag = control.Tag;

            Thread t = new Thread(new ThreadStart(ShowForm));
            t.SetApartmentState(ApartmentState.STA);
            t.Start();

            //t.Join(5000);

            //ShowForm();
        }

        private void ShowForm()
        {
            string outFileName = Guid.NewGuid().ToString();

            //try
            //{
            //ProcessHelper processHelper = new ProcessHelper("NoteHighLightForm.exe", new string[] { control.Tag, outFileName });
            //processHelper.IsWaitForInputIdle = true;
            //processHelper.ProcessStart();

            //CodeForm form = new CodeForm(tag, outFileName);
            //form.ShowDialog();

            //TestForm t = new TestForm();

            MainForm form = new MainForm(tag, outFileName);

            System.Windows.Forms.Application.Run(form);
            //}
            //catch (Exception ex)
            //{
            //    MessageBox.Show("Error executing NoteHighLightForm.exe：" + ex.Message);
            //    return;
            //}

            string fileName = Path.Combine(Path.GetTempPath(), outFileName + ".html");

            if (File.Exists(fileName))
            {
                InsertHighLightCodeToCurrentSide(fileName);
            }
        }



        /// <summary>
        /// Specified in Ribbon.xml, this method returns the image to display on the ribbon button
        /// </summary>
        /// <param name="imageName"></param>
        /// <returns></returns>
        public IStream GetImage(string imageName)
		{
			MemoryStream imageStream = new MemoryStream();
            //switch (imageName)
            //{
            //    case "CSharp.png":
            //        Properties.Resources.CSharp.Save(imageStream, ImageFormat.Png);
            //        break;
            //    default:
            //        Properties.Resources.Logo.Save(imageStream, ImageFormat.Png);
            //        break;
            //}

            BindingFlags flags = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            var b = typeof(Properties.Resources).GetProperty(imageName.Substring(0, imageName.IndexOf('.')), flags).GetValue(null, null) as Bitmap;
            b.Save(imageStream, ImageFormat.Png);

            return new CCOMStreamWrapper(imageStream);
		}

        /// <summary>
        /// 插入 HighLight Code 至滑鼠游標的位置
        /// Insert HighLight Code To Mouse Position  
        /// </summary>
        private void InsertHighLightCodeToCurrentSide(string fileName)
        {
            // Trace.TraceInformation(System.Reflection.MethodBase.GetCurrentMethod().Name);
            string htmlContent = File.ReadAllText(fileName, Encoding.UTF8);

            string notebookXml;
            try
            {
                OneNoteApplication.GetHierarchy(null, HierarchyScope.hsPages, out notebookXml, XMLSchema.xs2013);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Exception from onApp.GetHierarchy:" + ex.Message);
                return;
            }

            var doc = XDocument.Parse(notebookXml);
            ns = doc.Root.Name.Namespace;

            var pageNode = doc.Descendants(ns + "Page")
                              .Where(n => n.Attribute("isCurrentlyViewed") != null && n.Attribute("isCurrentlyViewed").Value == "true")
                              .FirstOrDefault();

            if (pageNode != null)
            {
                var existingPageId = pageNode.Attribute("ID").Value;

                string[] position = GetMousePointPosition(existingPageId);

                var page = InsertHighLightCode(htmlContent, position);
                page.Root.SetAttributeValue("ID", existingPageId);

                OneNoteApplication.UpdatePageContent(page.ToString(), DateTime.MinValue);
            }
        }

        /// <summary>
        /// 取得滑鼠所在的點
        /// Get Mouse Point
        /// </summary>
        private string[] GetMousePointPosition(string pageID)
        {
            string pageXml;
            OneNoteApplication.GetPageContent(pageID, out pageXml, PageInfo.piSelection);

            var node = XDocument.Parse(pageXml).Descendants(ns + "Outline")
                                               .Where(n => n.Attribute("selected") != null && n.Attribute("selected").Value == "partial")
                                               .FirstOrDefault();
            if (node != null)
            {
                var attrPos = node.Descendants(ns + "Position").FirstOrDefault();
                if (attrPos != null)
                {
                    var x = attrPos.Attribute("x").Value;
                    var y = attrPos.Attribute("y").Value;
                    return new string[] { x, y };
                }
            }
            return null;
        }

        /// <summary>
        /// 產生 XML 插入至 OneNote
        /// Generate XML Insert To OneNote
        /// </summary>
        public XDocument InsertHighLightCode(string htmlContent, string[] position)
        {
            XElement children = new XElement(ns + "OEChildren");

            var arrayLine = htmlContent.Split(new string[] { Environment.NewLine }, StringSplitOptions.None);
            foreach (var item in arrayLine)
            {
                //string s = item.Replace(@"style=""", string.Format(@"style=""font-family:{0}; ", GenerateHighlightContent.GenerateHighLight.Config.OutputArguments["Font"].Value));
                string s = string.Format(@"<body style=""font-family:{0}"">", GenerateHighlightContent.GenerateHighLight.Config.OutputArguments["Font"].Value) + 
                            HttpUtility.HtmlDecode(item) + "</body>";
                children.Add(new XElement(ns + "OE",
                                new XElement(ns + "T",
                                    new XCData(s))));
            }

            XElement outline = new XElement(ns + "Outline");

            if (position != null && position.Length == 2)
            {
                XElement pos = new XElement(ns + "Position");
                pos.Add(new XAttribute("x", position[0]));
                pos.Add(new XAttribute("y", position[1]));
                outline.Add(pos);
            }
            outline.Add(children);

            XElement page = new XElement(ns + "Page");
            page.Add(outline);

            XDocument doc = new XDocument();
            doc.Add(page);

            return doc;
        }

    }
}
