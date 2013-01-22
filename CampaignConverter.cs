using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Resources;
using System.Windows.Forms;
using PhDsoft.C4D.Platform.BusinessData;
using PhDsoft.C4D.Platform.BusinessLogic.ModelVerification;
using PhDsoft.C4D.Platform.BusinessLogic.StructureThickness;
using PhDsoft.C4D.Platform.UserInterface.VisualizationOptionsWindow;
using PhDsoft.C4D.SDK.Culture;
using PhDsoft.C4D.SDK.Math;
using PhDsoft.C4D.SDK.Resources;
using PhDsoft.C4D.SDK.Windows.Forms;
using PhDsoft.C4D.SDK.Windows.OpenGL;
using C4DView = PhDsoft.C4D.Platform.UserInterface.View;
using PhDsoft.C4D.Platform.BusinessLogic.Visualization;

namespace PhDsoft.C4D.MainApplication.BusinessData
{
	public class CampaignConverter
	{
		private double epsilon;
		private bool calculeAverage = true;
		private bool calculeAverageOnStiffeners = false;
		private float averageConvergenceTolerance = 0.5f;
		private float groupingLengthOnStiffeners = 3.0f;
        ResourceManager entitiesResourceManager;

		private Cad4DProgressBar progressBar = null;

		// average
		private Campaign averageCampaign;
		private CampaignReport currentAverageCampaignReport;

		// iacs
		//private Campaign iacsCampaign;
		//private CampaignReport currentIacsCampaignReport;

		private AverageStructureThicknessGaugingsCreator averageGaugingsCreator;

		private SteelThicknessGaugingPointVerifier steelThicknessGaugingPointVerifier = new SteelThicknessGaugingPointVerifier();

		private int bitmapCount;
		private GLPanel glPanel = null;

		private Dictionary<Guid, Dictionary<int, Bitmap>> sectionThumbnails = new Dictionary<Guid, Dictionary<int, Bitmap>>();

		private Dictionary<int, int> symmetricLongitudinalSections = null;
		//private Dictionary<string, List<string>> elementTypeNameCaptionProperties = null;

		public CampaignConverter()
		{
            string resourceName = "PhDsoft.C4D.MainApplication.UserInterface.Resources.EntitiesStrings";
            StringResourceManager.Instance.RegisterStringResource(resourceName, GetType().Assembly);
            this.entitiesResourceManager = StringResourceManager.Instance.GetResourceManager(resourceName);

			this.averageGaugingsCreator = new AverageStructureThicknessGaugingsCreator();
		}
		/// <summary>
		/// This instance can use convergence tolerance or not. Others parameters will use default values.
		/// </summary>
		public CampaignConverter(bool usingConvergenceTolerance)
		{
            string resourceName = "PhDsoft.C4D.MainApplication.UserInterface.Resources.EntitiesStrings";
            StringResourceManager.Instance.RegisterStringResource(resourceName, GetType().Assembly);
            this.entitiesResourceManager = StringResourceManager.Instance.GetResourceManager(resourceName);

			this.averageGaugingsCreator = new AverageStructureThicknessGaugingsCreator(usingConvergenceTolerance);
		}
		/// <summary>
		/// This instance always will use convergence tolerance.
		/// </summary>
		public CampaignConverter(bool calculeAverage, bool calculeAverageOnStiffeners, float averageConvergenceTolerance, float groupingLengthOnStiffeners)
		{
			this.calculeAverage = calculeAverage;
			this.calculeAverageOnStiffeners = calculeAverageOnStiffeners;
			this.averageConvergenceTolerance = (calculeAverage) ? averageConvergenceTolerance : 0.0f;
			this.groupingLengthOnStiffeners = groupingLengthOnStiffeners;

            string resourceName = "PhDsoft.C4D.MainApplication.UserInterface.Resources.EntitiesStrings";
            StringResourceManager.Instance.RegisterStringResource(resourceName, GetType().Assembly);
            this.entitiesResourceManager = StringResourceManager.Instance.GetResourceManager(resourceName);

			this.averageGaugingsCreator = new AverageStructureThicknessGaugingsCreator(calculeAverage, calculeAverageOnStiffeners, averageConvergenceTolerance, groupingLengthOnStiffeners);
		}

		private bool IsAverageCampaignAlreadyCreated(Campaign campaign)
		{
			try
			{
				List<Campaign> steelThicknessGaugingCampaigns = campaign.Vessel.GetCampaigns("SteelThicknessGaugingCampaign");
				foreach (Campaign steelThicknessGaugingCampaign in steelThicknessGaugingCampaigns)
				{
					if (!steelThicknessGaugingCampaign.IsConvertedCampaign)
					{
						continue;
					}

					if (steelThicknessGaugingCampaign.SourceCampaignID == campaign.ID)
					{
						return true;
					}
				}

				return false;
			}
			catch (Exception ex)
			{
				throw new ApplicationException("IsCampaignAlreadyCreated cannot be executed!", ex);
			}
		}
		private bool IsAverageCampaignReportAlreadyCreated(CampaignReport campaignReport, out Campaign createdIACSCampaign)
		{
			try
			{
				createdIACSCampaign = null;
				Campaign campaign = campaignReport.Campaign;

				List<Campaign> steelThicknessGaugingCampaigns = campaign.Vessel.GetCampaigns("SteelThicknessGaugingCampaign");
				foreach (Campaign steelThicknessGaugingCampaign in steelThicknessGaugingCampaigns)
				{
					if (!steelThicknessGaugingCampaign.IsConvertedCampaign)
					{
						continue;
					}

					if (steelThicknessGaugingCampaign.SourceCampaignID != campaign.ID)
					{
						continue;
					}

					createdIACSCampaign = steelThicknessGaugingCampaign;

					foreach (CampaignReport steelThicknessGaugingCampaignReport in steelThicknessGaugingCampaign.CampaignReports.Values)
					{
						if (steelThicknessGaugingCampaignReport.SourceCampaignReportID == campaignReport.ID)
						{
							return true;
						}
					}
				}

				return false;
			}
			catch (Exception ex)
			{
				throw new ApplicationException("IsCampaignAlreadyCreated cannot be executed!", ex);
			}
		}
		private void CreateAverageCampaign(Campaign sourceCampaign)
		{
			IDocument document = sourceCampaign.Vessel.Document;

			string campaignTypeName = "SteelThicknessGaugingCampaign";
			CampaignType campaignType = sourceCampaign.CampaignType;
			DateTime campaignDate = sourceCampaign.Date;
			this.averageCampaign = document.AddNewCampaign(campaignTypeName, campaignType, campaignDate, false, false, false, true);
			if (this.averageCampaign == null)
			{
				return;
			}

			this.averageCampaign.SourceCampaignID = sourceCampaign.ID;

			this.averageCampaign.Status = sourceCampaign.Status;
            this.averageCampaign.Name = this.entitiesResourceManager.GetString("AverageCampaignInitialName") + " " + sourceCampaign.Name;
			this.averageCampaign.CampaignSubType = this.averageCampaign.CampaignType.GetCampaignSubType("Average");

			document.DocumentStateController.BroadcastEntityCreation(document, this.averageCampaign);
		}

		public bool ToAverage(Campaign sourceCampaign)
		{
			try
			{
				#region verify parameters

				if (sourceCampaign == null)
				{
					throw new ArgumentNullException("sourceCampaign");
				}

				if (!sourceCampaign.CampaignType.IsGauging)
				{
					throw new ArgumentException("Campaign must be gauging campaign'");
				}

				#endregion

				Vessel vessel = sourceCampaign.Vessel;
				Document document = vessel.Document as Document;

				if (this.IsAverageCampaignAlreadyCreated(sourceCampaign))
				{
					MessageBox.Show(UserMessage.Show("Command.ConvertToAverageCommand.CampaignAlreadyConvertedMessage", "This campaign/campaign report is already converted to these parameters."),
									UserMessage.Show("Command.ConvertToAverageCommand.ProgressBarTitle", "Convert campaign/campaign report to Average"),
									MessageBoxButtons.OK, MessageBoxIcon.Exclamation);

					return false;
				}

				int countSections = 0;
				foreach (CampaignReport campaignReport in sourceCampaign.CampaignReports.Values)
				{
					List<Section> sections = document.GetSectionsWithPermanentElementsInCampaignReport(campaignReport);
					countSections += sections.Count;
				}

				if (countSections == 0)
				{
					MessageBox.Show(UserMessage.Show("Command.ConvertToAverageCommand.NotElementsToConvert", "This campaign/campaign report cannot be converted because is has not any element."),
									UserMessage.Show("Command.ConvertToAverageCommand.ProgressBarTitle", "Convert campaign/campaign report to Average"),
									MessageBoxButtons.OK, MessageBoxIcon.Exclamation);

					return false;
				}

				#region Showing the progress bar

				this.progressBar = new Cad4DProgressBar();
				this.progressBar.Text = "";
				this.progressBar.Title.Text = UserMessage.Show("Command.ConvertToAverageCommand.ProgressBarTitle", "Convert campaign/campaign report to Average");
				this.progressBar.Detail.Text = "";
				this.progressBar.Message.Text = UserMessage.Show("Command.ConvertToAverageCommand.ProgressBarMessage", "Converting campaign/campaign report to Average. Please wait...");
				this.progressBar.Bar.Minimum = 0;
				this.progressBar.Bar.Maximum = 100;
				this.progressBar.Bar.Value = 0;
				this.progressBar.TopMost = true;
				this.progressBar.Show();
				this.progressBar.BringToFront();
				this.progressBar.Refresh();

				#endregion

				document.UpdateDocument(sourceCampaign.Date);


				int barStep = (int)(100 / countSections);

				this.CreateAverageCampaign(sourceCampaign);
				if (this.averageCampaign == null)
				{
					return false;
				}

				this.symmetricLongitudinalSections = document.GetSymmetricLongitudinalSectionsInCampaign(sourceCampaign);

				foreach (CampaignReport campaignReport in sourceCampaign.CampaignReports.Values)
				{
					this.ToAverage(campaignReport, barStep);
				}

				// Change reference date to view gauging points created in IACS campaign
				document.DocumentStateController.DateTimeControl.DateTime = this.averageCampaign.Date;

				this.progressBar.Bar.Value = 100;
				this.progressBar.Show();
				this.progressBar.BringToFront();
				this.progressBar.Refresh();

				return true;
			}
			catch (Exception ex)
			{
				throw new ApplicationException("Campaign cannot be converted to IACS!", ex);
			}
			finally
			{
				#region Finalize progress bar

				if (this.progressBar != null)
				{
					this.progressBar.Bar.Value = 100;
					this.progressBar.Close();
					this.progressBar.Dispose();
				}

				#endregion
			}
		}
		public bool ToAverage(CampaignReport sourceCampaignReport)
		{
			try
			{
				#region verify parameters

				if (sourceCampaignReport == null)
				{
					throw new ArgumentNullException("sourceCampaignReport");
				}

				if (!sourceCampaignReport.Campaign.CampaignType.IsGauging)
				{
					throw new ArgumentException("Campaign must be gauging campaign'");
				}

				#endregion

				Campaign sourceCampaign = sourceCampaignReport.Campaign;
				Vessel vessel = sourceCampaign.Vessel;
				Document document = vessel.Document as Document;

				Campaign createdAverageCampaign = null;
				if (this.IsAverageCampaignReportAlreadyCreated(sourceCampaignReport, out createdAverageCampaign))
				{
					MessageBox.Show(UserMessage.Show("Command.ConvertToAverageCommand.CampaignAlreadyConvertedMessage", "This campaign/campaign report is already converted to these parameters."),
														UserMessage.Show("Command.ConvertToAverageCommand.ProgressBarTitle", "Convert campaign/campaign report to Average"),
														MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
					return false;
				}

				List<Section> sections = document.GetSectionsWithPermanentElementsInCampaignReport(sourceCampaignReport);
				int countSections = sections.Count;

				if (countSections == 0)
				{
					MessageBox.Show(UserMessage.Show("Command.ConvertToAverageCommand.NotElementsToConvert", "This campaign/campaign report cannot be converted because is has not any element."),
									UserMessage.Show("Command.ConvertToAverageCommand.ProgressBarTitle", "Convert campaign/campaign report to Average"),
									MessageBoxButtons.OK, MessageBoxIcon.Exclamation);

					return false;
				}

				#region Showing the progress bar

				this.progressBar = new Cad4DProgressBar();
				this.progressBar.Text = "";
				this.progressBar.Title.Text = UserMessage.Show("Command.ConvertToAverageCommand.ProgressBarTitle", "Convert campaign to Average");
				this.progressBar.Detail.Text = "";
				this.progressBar.Message.Text = UserMessage.Show("Command.ConvertToAverageCommand.ProgressBarMessage", "Converting campaign to Average. Please wait...");
				this.progressBar.Bar.Minimum = 0;
				this.progressBar.Bar.Maximum = 100;
				this.progressBar.Bar.Value = 0;
				this.progressBar.TopMost = true;
				this.progressBar.Show();
				this.progressBar.BringToFront();
				this.progressBar.Refresh();

				#endregion

				document.UpdateDocument(sourceCampaign.Date);

				int barStep = (int)(100 / countSections);

				bool isAlreadyCreatedAverageCampaign = createdAverageCampaign != null;
				if (!isAlreadyCreatedAverageCampaign)
				{
					this.CreateAverageCampaign(sourceCampaign);
					if (this.averageCampaign == null)
					{
						return false;
					}
				}
				else
				{
					this.averageCampaign = createdAverageCampaign;
				}

				this.symmetricLongitudinalSections = document.GetSymmetricLongitudinalSectionsInCampaignReport(sourceCampaignReport);
				this.ToAverage(sourceCampaignReport, barStep);

				// Change reference date to view gauging points created in IACS campaign
				document.DocumentStateController.DateTimeControl.DateTime = this.averageCampaign.Date;

				progressBar.Bar.Value = 100;
				progressBar.Show();
				progressBar.BringToFront();
				progressBar.Refresh();

				return true;
			}
			catch (Exception ex)
			{
				throw new ApplicationException("Campaign cannot be converted to IACS!", ex);
			}
			finally
			{
				#region Finalize progress bar

				if (progressBar != null)
				{
					progressBar.Bar.Value = 100;
					progressBar.Close();
					progressBar.Dispose();
				}

				#endregion
			}
		}
		private void ToAverage(CampaignReport sourceCampaignReport, int barStep)
		{
			try
			{
				Vessel vessel = sourceCampaignReport.Campaign.Vessel;
				Document document = vessel.Document as Document;

				string campaignTypeName = "SteelThicknessGaugingCampaign";
				CampaignReport averageCampaignReport = document.AddNewCampaignReport(this.averageCampaign, campaignTypeName, false);
                averageCampaignReport.Name = this.entitiesResourceManager.GetString("AverageCampaignInitialName") + " " + sourceCampaignReport.Name;
				averageCampaignReport.SourceCampaignReportID = sourceCampaignReport.ID;
				averageCampaignReport.SourceCampaignReportGUID = sourceCampaignReport.GUID;

				this.currentAverageCampaignReport = averageCampaignReport;

				SortedList<int, SortedList<string, IList>> sectionElements = document.GetSectionsPermanentElementsInCampaignReport(sourceCampaignReport);

				// List to control sections already computed
				List<int> sectionIDs = new List<int>(sectionElements.Keys);

				int index = 1;
				int totalSections = sectionIDs.Count;

				foreach (int sectionID in sectionElements.Keys)
				{
					if (!sectionIDs.Contains(sectionID))
					{
						continue;
					}

					Section section = vessel.GetSection(sectionID);
					if (section == null || section.SectionType == null || section.SectionType.IsNameNull)
					{
						continue; // Exception?
					}

					int initialIacsID = this.DefineInitialIACSID(section) + 1;

					this.progressBar.Bar.Value += barStep;
					this.progressBar.Detail.Text = "(" + index.ToString() + "/" + totalSections.ToString() + ") " + section.Name;
					this.progressBar.Show();
					this.progressBar.BringToFront();
					this.progressBar.Refresh();

					index++;

					// ToDo: Think in a correct solution
					this.epsilon = (section.BoundingBox.YMax - section.BoundingBox.YMin) * 0.02;

					if (!sectionElements[sectionID].ContainsKey("SteelThicknessGaugingPoint"))
					{
						continue;
					}

					switch (section.SectionType.Name)
					{
						case "WebFrames":
						case "TransverseBulkheads":
						case "TransverseCentralBulkheads":
						case "SwashBulkheads":
						{
							#region Non Longitudinal

							this.ToAverageInNonLongitudinalSection(section, sourceCampaignReport, ref initialIacsID);
							break;

							#endregion
						}
						case "MainDeck":
						case "OtherDecks":
						case "InnerBottom":
						case "Bottom":
						{
							#region Decks and Inner Bottom

							this.ToAverageInDecksSection(section, sourceCampaignReport, ref initialIacsID);
							break;

							#endregion
						}
						case "Shell":
						case "Longitudinal":
						case "LongitudinalBulkheads":
						case "LongitudinalCentralBulkheads":
						{
							#region Symmetric Longitudinal

							Section symmetricSection = null;
							if (this.symmetricLongitudinalSections.ContainsKey(sectionID))
							{
								symmetricSection = vessel.GetSection(this.symmetricLongitudinalSections[sectionID]);
								if (symmetricSection != null)
								{
									sectionIDs.Remove(symmetricSection.ID);
								}
							}

							this.ToAverageInLongitudinalSection(section, symmetricSection, sourceCampaignReport, ref initialIacsID);
							break;

							#endregion
						}
						case "AnyOtherSection":
						{
							#region Others Section Types

							this.ToAverageInOtherTypesSection(section, sourceCampaignReport, ref initialIacsID);
							break;

							#endregion
						}
					}

					sectionIDs.Remove(sectionID);
					document.ReApplyCampaigns(section);
				}

				document.DocumentStateController.BroadcastEntityCreation(document, averageCampaignReport);
			}
			catch (Exception ex)
			{
				throw new ApplicationException("Campaign cannot be converted to IACS!", ex);
			}
		}

		private void ToAverageInNonLongitudinalSection(Section section, CampaignReport sourceCampaignReport, ref int initialIacsID)
		{
			try
			{
				Dictionary<string, Dictionary<ISectionElement, List<IGaugingElement>>> gaugingPointsByStructureElementType = section.GetGaugingElementsBySectionElementInCampaignReport("SteelThicknessGaugingPoint", sourceCampaignReport);
				if (gaugingPointsByStructureElementType == null || gaugingPointsByStructureElementType.Count == 0)
					return;

				List<SteelThicknessGaugingPoint> averageGaugingPoints = null;
				this.averageGaugingsCreator.CreateAverageGaugingPointsInNonLongitudinalSection(section, gaugingPointsByStructureElementType, false, out averageGaugingPoints);

				this.AddAverageGaugingPointsInSection(section, averageGaugingPoints);
			}
			catch (Exception ex)
			{
				throw new ApplicationException("Cannot create average gauging points in Non Longitudinal Section!", ex);
			}
		}
		private void ToAverageInDecksSection(Section section, CampaignReport sourceCampaignReport, ref int initialIacsID)
		{
			try
			{
				Dictionary<string, Dictionary<ISectionElement, List<IGaugingElement>>> gaugingPointsByStructureElementType = section.GetGaugingElementsBySectionElementInCampaignReport("SteelThicknessGaugingPoint", sourceCampaignReport);
				if (gaugingPointsByStructureElementType == null || gaugingPointsByStructureElementType.Count == 0)
					return;

				List<SteelThicknessGaugingPoint> averageGaugingPoints = null;
				this.averageGaugingsCreator.CreateAverageGaugingPointsInDeckSection(section, gaugingPointsByStructureElementType, out averageGaugingPoints);

				this.AddAverageGaugingPointsInSection(section, averageGaugingPoints);
			}
			catch (Exception ex)
			{
				throw new ApplicationException("Campaign cannot be converted to IACS in Main Deck Section!", ex);
			}
		}
		private void ToAverageInLongitudinalSection(Section section1, Section section2, CampaignReport sourceCampaignReport, ref int initialIacsID)
		{
			try
			{
				if (section1 == null && section2 == null)
					return;

				Dictionary<string, Dictionary<ISectionElement, List<IGaugingElement>>> gaugingPointsByStructureElementTypeInSection1 = null;
				Dictionary<string, Dictionary<ISectionElement, List<IGaugingElement>>> gaugingPointsByStructureElementTypeInSection2 = null;

				if (section1 != null)
				{
					gaugingPointsByStructureElementTypeInSection1 = section1.GetGaugingElementsBySectionElementInCampaignReport("SteelThicknessGaugingPoint", sourceCampaignReport);
				}
				if (section2 != null)
				{
					gaugingPointsByStructureElementTypeInSection2 = section2.GetGaugingElementsBySectionElementInCampaignReport("SteelThicknessGaugingPoint", sourceCampaignReport);
				}

				List<SteelThicknessGaugingPoint> averageGaugingPointsInSection1 = null;
				List<SteelThicknessGaugingPoint> averageGaugingPointsInSection2 = null;

				this.averageGaugingsCreator.CreateAverageGaugingPointsInLongitudinalSection(section1, section2, gaugingPointsByStructureElementTypeInSection1, gaugingPointsByStructureElementTypeInSection2, out averageGaugingPointsInSection1, out averageGaugingPointsInSection2);

				this.AddAverageGaugingPointsInSection(section1, averageGaugingPointsInSection1);
				this.AddAverageGaugingPointsInSection(section2, averageGaugingPointsInSection2);
			}
			catch (Exception ex)
			{
				throw new ApplicationException("Campaign cannot be converted to IACS in Symmetric Longitudinal Section!", ex);
			}
		}
		private void ToAverageInOtherTypesSection(Section section, CampaignReport sourceCampaignReport, ref int initialIacsID)
		{
			try
			{
				Dictionary<string, Dictionary<ISectionElement, List<IGaugingElement>>> gaugingPointsByStructureElementType = section.GetGaugingElementsBySectionElementInCampaignReport("SteelThicknessGaugingPoint", sourceCampaignReport);
				if (gaugingPointsByStructureElementType == null || gaugingPointsByStructureElementType.Count == 0)
					return;

				List<SteelThicknessGaugingPoint> averageGaugingPoints = null;
				this.averageGaugingsCreator.CreateAverageGaugingPointsInNonLongitudinalSection(section, gaugingPointsByStructureElementType, true, out averageGaugingPoints);

				this.AddAverageGaugingPointsInSection(section, averageGaugingPoints);
			}
			catch (Exception ex)
			{
				throw new ApplicationException("Cannot create average gauging points in Other Types Section!", ex);
			}
		}

		private void AddAverageGaugingPointsInSection(Section section, List<SteelThicknessGaugingPoint> averageGaugingPoints)
		{
			if (averageGaugingPoints != null)
			{
				foreach (SteelThicknessGaugingPoint averageGaugingPoint in averageGaugingPoints)
				{
					averageGaugingPoint.Name = averageGaugingPoint.IACSReportLocationData.ID.ToString();
					averageGaugingPoint.Section = section;
					averageGaugingPoint.Campaign = this.averageCampaign;
					averageGaugingPoint.CampaignReport = this.currentAverageCampaignReport;

					section.AddPermanentDomainEntityToGroup(averageGaugingPoint, true, false);
					this.steelThicknessGaugingPointVerifier.DoVerifyCompartments(averageGaugingPoint);
				}
			}
		}

		private void DefineColumnAndComplement(string columnAndComplement, out int column, out string complement)
		{
			try
			{
				string columnString = "";
				complement = "";

				foreach (char c in columnAndComplement)
				{
					if (char.IsNumber(c) || c == '-')
					{
						columnString += c;
					}
					else
					{
						complement += c;
					}
				}

				column = Convert.ToInt32(columnString);
			}
			catch (Exception ex)
			{
				throw new ApplicationException("Column and Complement cannot be defined!", ex);
			}
		}
		private int DefineInitialIACSID(Section section)
		{
			int initialID = 0;

			IList steelThicknessGaugingPointsInCampaign = null;
			//section.GetPermanentElementsInCampaign(this.iacsCampaign, false, "SteelThicknessGaugingPoint");
			section.GetPermanentElementsInCampaign(this.averageCampaign, false, "SteelThicknessGaugingPoint");

			if (steelThicknessGaugingPointsInCampaign != null && steelThicknessGaugingPointsInCampaign.Count > 0)						
			{
				foreach (SteelThicknessGaugingPoint steelThicknessGaugingPoint in steelThicknessGaugingPointsInCampaign)
				{
					if (!steelThicknessGaugingPoint.IACSReportLocationData.IsIDNull && steelThicknessGaugingPoint.IACSReportLocationData.ID > initialID)
					{
						initialID = steelThicknessGaugingPoint.IACSReportLocationData.ID;
					}
				}
			}

			return initialID;
		}
		
		private SortedList<double, Compartment> IntersectCompartments(Section section, Plane plane)
		{
			try
			{
				if (section.CompartmentComponents == null)
				{
					throw new NullReferenceException("compartmentComponents");
				}

				Point3D point = new Point3D(section.BoundingBox.MinPoint.X, section.BoundingBox.Center.Y, section.BoundingBox.Center.Z);
				SortedList<double, Compartment> intersectedCompartments = new SortedList<double, Compartment>();

				List<int> computedCompartments = new List<int>();
				foreach (CompartmentComponent compartmentComponent in section.CompartmentComponents.Values)
				{
					if (computedCompartments.Contains(compartmentComponent.CompartmentID))
					{
						continue;
					}

					computedCompartments.Add(compartmentComponent.CompartmentID);

					for (int i = 0; i < compartmentComponent.Design.Geometry.PrimitivesCount; i++)
					{
						IGeometricPrimitiveRegion primitive = compartmentComponent.Design.Geometry.Primitive(i) as IGeometricPrimitiveRegion;
						if (primitive != null)
						{
							List<IGeometricPrimitive> compartmentComponentIntersection = primitive.IntersectPlane(plane);
							if (compartmentComponentIntersection.Count > 0)
							{
								double distance = compartmentComponent.Compartment.BoundingBox.Center.PointDistance(point);
								while (intersectedCompartments.ContainsKey(distance))
								{
									distance += Point3D.Epsilon;
								}
								intersectedCompartments.Add(distance, compartmentComponent.Compartment);
							}
						}
					}
				}

				return intersectedCompartments;
			}
			catch (Exception ex)
			{
				throw new ApplicationException("Compartments cannot be intersected!", ex);
			}
		}
		private List<SteelThicknessGaugingPoint> GetPointsBetweenPlanes(List<SteelThicknessGaugingPoint> steelThicknessGaugingPoints, Plane minPlane, Plane maxPlane)
		{
			try
			{
				List<SteelThicknessGaugingPoint> pointsBetweenPlanes = new List<SteelThicknessGaugingPoint>();
				foreach (SteelThicknessGaugingPoint steelThicknessGaugingPoint in steelThicknessGaugingPoints)
				{
					Point3D point = steelThicknessGaugingPoint.PointGeometry.Position;
					if (minPlane.Signal(point) == 1 && maxPlane.Signal(point) == -1)
					{
						pointsBetweenPlanes.Add(steelThicknessGaugingPoint);
					}
				}

				return pointsBetweenPlanes;
			}
			catch (Exception ex)
			{
				throw new ApplicationException("Points between planes cannot be get!", ex);
			}
		}

		private SortedList<int, List<SteelThicknessGaugingPoint>> OrganizeIACSGaugingPointsByID(List<SteelThicknessGaugingPoint> steelThicknessGaugingPoints)
		{
			try
			{
				SortedList<int, List<SteelThicknessGaugingPoint>> organizedGaugingPoints = new SortedList<int, List<SteelThicknessGaugingPoint>>();

				if (steelThicknessGaugingPoints == null || steelThicknessGaugingPoints.Count == 0)
				{
					return organizedGaugingPoints;
				}

				foreach (SteelThicknessGaugingPoint steelThicknessGaugingPoint in steelThicknessGaugingPoints)
				{
					if (steelThicknessGaugingPoint.IACSReportLocationData.IsIDNull)
					{
						throw new NullReferenceException("IACSReportLocationDataID");
					}

					if (!organizedGaugingPoints.ContainsKey((int)steelThicknessGaugingPoint.IACSReportLocationData.ID))
					{
						organizedGaugingPoints.Add((int)steelThicknessGaugingPoint.IACSReportLocationData.ID, new List<SteelThicknessGaugingPoint>());
					}

					organizedGaugingPoints[(int)steelThicknessGaugingPoint.IACSReportLocationData.ID].Add(steelThicknessGaugingPoint);
				}

				return organizedGaugingPoints;
			}
			catch (Exception ex)
			{
				throw new ApplicationException("IACS Gauging points cannot be organized by ID", ex);
			}
		}

		public Size GetImageSize()
		{
			return this.glPanel.Size;
		}

		public List<string> CreateSketch(Section section, Campaign campaign)
		{
			List<string> bitmaps = new List<string>();
			foreach (CampaignReport campaignReport in campaign.CampaignReports.Values)
			{
				List<string> campaignReportBitmaps = CreateSketch(section, campaignReport);
				if (campaignReportBitmaps != null && campaignReportBitmaps.Count > 0)
				{
					bitmaps.AddRange(campaignReportBitmaps);
				}
			}

			return bitmaps;
		}
		public List<string> CreateSketch(Section section, CampaignReport campaignReport)
		{
			try
			{
				if (section == null)
				{
					throw new ArgumentNullException("section");
				}

				if (campaignReport == null)
				{
					throw new ArgumentNullException("campaignReport");
				}

				SortedList<string, IList> elementsInCampaign = section.GetPermanentElementsInCampaignReport(campaignReport);
				if (!elementsInCampaign.ContainsKey("SteelThicknessGaugingPoint") || elementsInCampaign["SteelThicknessGaugingPoint"].Count == 0)
				{
					return null;
				}

				SortedList<int, List<SteelThicknessGaugingPoint>> pointsPerSketch;
				List<SteelThicknessGaugingPoint> steelThicknessGaugingPoints = elementsInCampaign["SteelThicknessGaugingPoint"].Cast<SteelThicknessGaugingPoint>().ToList();

				return this.CreateSketch(section, campaignReport, steelThicknessGaugingPoints, out pointsPerSketch);
			}
			catch (Exception ex)
			{
				throw new ApplicationException("Sketch for section/campaign report cannot be created!", ex);
			}
		}

		public List<string> CreateSketch(Section section, Section symmetricSection, Campaign campaign)
		{
			List<string> bitmaps = new List<string>();
			foreach (CampaignReport campaignReport in campaign.CampaignReports.Values)
			{
				List<string> campaignReportBitmaps = CreateSketch(section, symmetricSection, campaignReport);
				if (campaignReportBitmaps != null && campaignReportBitmaps.Count > 0)
				{
					bitmaps.AddRange(campaignReportBitmaps);
				}
			}

			return bitmaps;
		}
		public List<string> CreateSketch(Section section, Section symmetricSection, CampaignReport campaignReport)
		{
			try
			{
				if (section == null)
				{
					throw new ArgumentNullException("section");
				}

				if (symmetricSection == null)
				{
					throw new ArgumentNullException("symmetricSection");
				}

				if (campaignReport == null)
				{
					throw new ArgumentNullException("campaignReport");
				}

				if (!section.SectionType.Name.Equals("Shell") && !section.SectionType.Name.Equals("Longitudinal") &&
					!section.SectionType.Name.Equals("LongitudinalBulkheads") && !symmetricSection.SectionType.Name.Equals("Shell") &&
					!symmetricSection.SectionType.Name.Equals("Longitudinal") && !symmetricSection.SectionType.Name.Equals("LongitudinalBulkheads")
					)
				{
					return null;
				}

				SortedList<string, IList> sectionElementsInCampaign = section.GetPermanentElementsInCampaignReport(campaignReport);
				SortedList<string, IList> symmetricElementsInCampaign = symmetricSection.GetPermanentElementsInCampaignReport(campaignReport);

				List<SteelThicknessGaugingPoint> sectionSteelThicknessGaugingPoints = (sectionElementsInCampaign.ContainsKey("SteelThicknessGaugingPoint")) ? sectionElementsInCampaign["SteelThicknessGaugingPoint"].Cast<SteelThicknessGaugingPoint>().ToList() : null;
				List<SteelThicknessGaugingPoint> symmetricSteelThicknessGaugingPoints = (symmetricElementsInCampaign.ContainsKey("SteelThicknessGaugingPoint")) ? symmetricElementsInCampaign["SteelThicknessGaugingPoint"].Cast<SteelThicknessGaugingPoint>().ToList() : null;

				List<SteelThicknessGaugingPoint> steelThicknessGaugingPoints = new List<SteelThicknessGaugingPoint>();
				if (sectionSteelThicknessGaugingPoints != null && sectionSteelThicknessGaugingPoints.Count > 0)
				{
					steelThicknessGaugingPoints.AddRange(sectionSteelThicknessGaugingPoints);
				}
				if (symmetricSteelThicknessGaugingPoints != null && symmetricSteelThicknessGaugingPoints.Count > 0)
				{
					steelThicknessGaugingPoints.AddRange(symmetricSteelThicknessGaugingPoints);
				}

				SortedList<int, List<SteelThicknessGaugingPoint>> pointsPerSketch;
				return this.CreateSketch(section, symmetricSection, campaignReport, steelThicknessGaugingPoints, out pointsPerSketch);
			}
			catch (Exception ex)
			{
				throw new ApplicationException("Sketch for section/campaign report cannot be created!", ex);
			}
		}

		public List<string> CreateSketch(Section section, CampaignReport campaignReport, List<SteelThicknessGaugingPoint> steelThicknessGaugingPoints)
		{
			SortedList<int, List<SteelThicknessGaugingPoint>> pointsPerSketch;
			return this.CreateSketch(section, campaignReport, steelThicknessGaugingPoints, out pointsPerSketch);
		}
		public List<string> CreateSketch(Section section, Section symmetricSection, CampaignReport campaignReport, List<SteelThicknessGaugingPoint> steelThicknessGaugingPoints)
		{
			SortedList<int, List<SteelThicknessGaugingPoint>> pointsPerSketch;
			return this.CreateSketch(section, symmetricSection, campaignReport, steelThicknessGaugingPoints, out pointsPerSketch);
		}

		public List<string> CreateSketch(Section section, Campaign campaign, List<SteelThicknessGaugingPoint> steelThicknessGaugingPoints, out SortedList<int, List<SteelThicknessGaugingPoint>> pointsPerSketch)
		{
			int index = 1;
			List<string> bitmaps = new List<string>();
			SortedList<int, List<SteelThicknessGaugingPoint>> campaignReportPointsPerSketch;

			pointsPerSketch = new SortedList<int, List<SteelThicknessGaugingPoint>>();

			foreach (CampaignReport campaignReport in campaign.CampaignReports.Values)
			{
				List<string> campaignReportBitmaps = CreateSketch(section, campaignReport, steelThicknessGaugingPoints, out campaignReportPointsPerSketch);
				if (campaignReportBitmaps != null && campaignReportBitmaps.Count > 0)
				{
					bitmaps.AddRange(campaignReportBitmaps);

					foreach (List<SteelThicknessGaugingPoint> points in campaignReportPointsPerSketch.Values)
					{
						pointsPerSketch.Add(index, points);
						index++;
					}
				}
			}

			return bitmaps;
		}
		public List<string> CreateSketch(Section section, CampaignReport campaignReport, List<SteelThicknessGaugingPoint> steelThicknessGaugingPoints, out SortedList<int, List<SteelThicknessGaugingPoint>> pointsPerSketch)
		{
			try
			{
				if (section == null)
				{
					throw new ArgumentNullException("section");
				}

				if (campaignReport == null)
				{
					throw new ArgumentNullException("campaignReport");
				}

				int index = 0;
				pointsPerSketch = new SortedList<int, List<SteelThicknessGaugingPoint>>();

				SortedList<string, IList> elementsInCampaign = section.GetPermanentElementsInCampaignReport(campaignReport);
				if (!elementsInCampaign.ContainsKey("SteelThicknessGaugingPoint") || elementsInCampaign["SteelThicknessGaugingPoint"].Count == 0)
				{
					return null;
				}

				Document document = campaignReport.Vessel.Document as Document;
				Vessel vessel = document.Vessel;

				List<string> bitmaps = new List<string>();

				string sectionDrawerCurrentViewModeName = section.SectionDrawer.CurrentViewModeName;
				section.SectionDrawer.CurrentViewModeName = "SketchFormat";

				if (!this.sectionThumbnails.ContainsKey(vessel.GUID))
				{
					this.sectionThumbnails.Add(vessel.GUID, new Dictionary<int, Bitmap>());
				}
				if (!this.sectionThumbnails[vessel.GUID].ContainsKey(section.ID))
				{
					this.sectionThumbnails[vessel.GUID].Add(section.ID, null);
				}
				if (this.sectionThumbnails[vessel.GUID][section.ID] == null)
				{
					CaptionsModel.Instance.SetCaptionPropertySelection("Plate", "IACSSketchDescription", false);

					section.SectionDrawer.CurrentViewModeName = "Wireframe";

					section.SectionDrawer.ConfigureView(this.glPanel, section.GetDefaultView(), true);
					section.SectionDrawer.UpdateLastCamera(this.glPanel);


					section.SectionDrawer.DrawAxis = false;
					bool isWireframeVisible = ViewModesData.Instance.IsVisible(section.SectionDrawer.CurrentViewModeName, "Wireframe");
					if (!isWireframeVisible)
					{
						ViewModesData.Instance.SetVisibility(section.SectionDrawer.CurrentViewModeName, "Wireframe", true, true);
					}

					Bitmap bitmap = section.SectionDrawer.DrawModelImmediateMode(this.glPanel);

					if (!isWireframeVisible)
					{
						ViewModesData.Instance.SetVisibility(section.SectionDrawer.CurrentViewModeName, "Wireframe", false, true);
					}
					section.SectionDrawer.DrawAxis = true;


					this.sectionThumbnails[vessel.GUID][section.ID] = bitmap;

					section.SectionDrawer.CurrentViewModeName = "SketchFormat";

					CaptionsModel.Instance.SetCaptionPropertySelection("Plate", "IACSSketchDescription", true);
				}

				Bitmap originalThumbnail = this.sectionThumbnails[vessel.GUID][section.ID];

				if (section.SectionType.Name.Equals("WebFrames") ||
					section.SectionType.Name.Equals("TransverseBulkheads") ||
					section.SectionType.Name.Equals("TransverseCentralBulkheads") ||
					section.SectionType.Name.Equals("SwashBulkheads")
				   )
				{
					#region Non Longitudinal

					pointsPerSketch.Add(index, steelThicknessGaugingPoints);
					index++;

					BoundingBox boundingBox = section.GetBoundingBoxOfElementType("Plate");

					Vector3D planeNormal = section.Normal * section.Up;
					Point3D planePoint = boundingBox.Center;
					Plane plane = new Plane(planeNormal, planePoint);

					List<SteelThicknessGaugingPoint> backGaugingPoints = null;
					List<SteelThicknessGaugingPoint> frontGaugingPoints = null;

					this.averageGaugingsCreator.SeparateGaugingPoints(steelThicknessGaugingPoints, plane, out backGaugingPoints, out frontGaugingPoints);

					SortedList<int, List<SteelThicknessGaugingPoint>> backOrganizedGaugingPoints = this.OrganizeIACSGaugingPointsByID(backGaugingPoints);
					SortedList<int, List<SteelThicknessGaugingPoint>> frontOrganizedGaugingPoints = this.OrganizeIACSGaugingPointsByID(frontGaugingPoints);

					#region Clone gauging points that only exists in source side, and copy to target side

					List<SteelThicknessGaugingPoint> pointsToBeDeleted = new List<SteelThicknessGaugingPoint>();

					List<SteelThicknessGaugingPoint> sourceGaugingPoints = (backGaugingPoints.Count < frontGaugingPoints.Count) ? backGaugingPoints : frontGaugingPoints;
					List<SteelThicknessGaugingPoint> targetGaugingPoints = (backGaugingPoints.Count < frontGaugingPoints.Count) ? frontGaugingPoints : backGaugingPoints;

					List<int> sourceGaugingPointsIACSID = (backGaugingPoints.Count < frontGaugingPoints.Count) ? new List<int>(backOrganizedGaugingPoints.Keys) : new List<int>(frontOrganizedGaugingPoints.Keys);
					List<int> targetGaugingPointsIACSID = (backGaugingPoints.Count < frontGaugingPoints.Count) ? new List<int>(frontOrganizedGaugingPoints.Keys) : new List<int>(backOrganizedGaugingPoints.Keys);

					foreach (SteelThicknessGaugingPoint sourceGaugingPoint in sourceGaugingPoints)
					{
						if (targetGaugingPointsIACSID.Contains((int)sourceGaugingPoint.IACSReportLocationData.ID))
						{
							continue;
						}

						Point3D pointPosition = new Point3D(sourceGaugingPoint.PointGeometry.Position);
						pointPosition = (Point3D)pointPosition.Mirror(plane);
						Vector3D pointNormal = new Vector3D(sourceGaugingPoint.PointGeometry.Normal);

						int geometryID, geometryPartID;
						List<string> elementTypeNames = new List<string>();
						elementTypeNames.Add(sourceGaugingPoint.TargetElement.GetBaseType().Name);
						Point3D closestPoint;
						SectionElement sectionElement = section.PointOverDomainEntity(pointPosition, elementTypeNames, 0.01, out closestPoint, out geometryID, out geometryPartID) as SectionElement;
						if (sectionElement == null)
						{
							continue;
						}

						AverageSteelThicknessGaugingPoint averageSteelThicknessGaugingPoint = new AverageSteelThicknessGaugingPoint();

						averageSteelThicknessGaugingPoint.PointGeometry.Position = pointPosition;
						averageSteelThicknessGaugingPoint.PointGeometry.Normal = pointNormal;

						averageSteelThicknessGaugingPoint.Section = section;
						averageSteelThicknessGaugingPoint.Campaign = campaignReport.Campaign;
						averageSteelThicknessGaugingPoint.CampaignReport = campaignReport;
						averageSteelThicknessGaugingPoint.TargetElement = sectionElement as IWorkableElement;
						averageSteelThicknessGaugingPoint.TargetElementGeometryID = geometryID;
						averageSteelThicknessGaugingPoint.TargetElementGeometrySegmentID = geometryPartID;
						averageSteelThicknessGaugingPoint.TargetElementPart = sourceGaugingPoint.TargetElementPart;
						averageSteelThicknessGaugingPoint.Active = true;
						averageSteelThicknessGaugingPoint.GaugingValue = sourceGaugingPoint.GaugingValue;

						averageSteelThicknessGaugingPoint.IACSReportLocationData.ID = sourceGaugingPoint.IACSReportLocationData.ID;
						averageSteelThicknessGaugingPoint.IACSReportLocationData.SetColumnNull();
						averageSteelThicknessGaugingPoint.IACSReportLocationData.Strake = sourceGaugingPoint.IACSReportLocationData.Strake;
						averageSteelThicknessGaugingPoint.IACSReportLocationData.IsPortSide = !(bool)sourceGaugingPoint.IACSReportLocationData.IsPortSide;

						averageSteelThicknessGaugingPoint.Name = averageSteelThicknessGaugingPoint.IACSReportLocationData.ID.ToString();

						//#if (DEBUG)
						//                        newFrontGaugingPoint.Name += ((bool)newFrontGaugingPoint.IACSReportLocationData.IsPortSide) ? " P " : " S ";
						//#endif

						section.AddPermanentDomainEntityToGroup(averageSteelThicknessGaugingPoint, true, false);
						//section.Vessel.Document.DocumentStateController.BroadcastEntityCreation(this, averageSteelThicknessGaugingPoint);

						targetGaugingPoints.Add(averageSteelThicknessGaugingPoint);
						pointsToBeDeleted.Add(averageSteelThicknessGaugingPoint);
					}

					#endregion

					List<SectionElement> sectionElements = this.GetSectionElementsVisibleInSketch(section, targetGaugingPoints);

					// Generate thumbnail
					Bitmap thumbnail = originalThumbnail.Clone() as Bitmap;

					Rectangle rectangle = this.GetRectangleOfRenderizedArea(sectionElements);
					for (int i = 0; i < thumbnail.Width; i++)
					{
						for (int j = 0; j < thumbnail.Height; j++)
						{
							if (this.IsPixelOfRectangle(i, j, rectangle, 3))
							{
								thumbnail.SetPixel(i, j, Color.Red);
							}
						}
					}

					//int newWidht = (int)(thumbnail.Width / 5);
					//int newHeight = (int)(thumbnail.Height / 5);
					//Image image = thumbnail.GetThumbnailImage(newWidht, newHeight, null, IntPtr.Zero);
					//thumbnail = new Bitmap(image);

					string thumbName = thumbnail.RawFormat.Guid.ToString() + "-" + this.bitmapCount.ToString() + ".jpg";
					this.bitmapCount++;

					thumbName = document.HDF5DataAccessController.SaveBitmap(thumbnail, thumbName);
					bitmaps.Add(thumbName);

					//section.SectionDrawer.CurrentViewModeName = "SketchFormat";

					C4DView view = section.SectionDrawer.ComputeViewForSectionElements(this.glPanel, sectionElements, false, 1.2f, true);
					if (view == null)
					{
						view = section.GetDefaultView();
					}

					section.SectionDrawer.ConfigureView(this.glPanel, view, false);
					section.SectionDrawer.UpdateLastCamera(this.glPanel);

					// Get IDs of gauging points to set in SectionDrawer
					List<int> pointIDs = new List<int>();
					foreach (SteelThicknessGaugingPoint steelThicknessGaugingPoint in targetGaugingPoints)
					{
						pointIDs.Add(steelThicknessGaugingPoint.ID);
					}

					section.SectionDrawer.SetPriorityVisibleElements("SteelThicknessGaugingPoint", pointIDs);

					Bitmap bitmap = section.SectionDrawer.DrawModelImmediateModeCuttingOfCompartment(this.glPanel);

					section.SectionDrawer.ClearPriorityVisibleElements("SteelThicknessGaugingPoint");

					string bitmapName = bitmap.RawFormat.Guid.ToString() + "-" + this.bitmapCount.ToString() + ".jpg";
					this.bitmapCount++;

					bitmapName = document.HDF5DataAccessController.SaveBitmap(bitmap, bitmapName);
					bitmaps.Add(bitmapName);

					bitmap.Dispose();

					foreach (SteelThicknessGaugingPoint pointToBeDeleted in pointsToBeDeleted)
					{
						document.DeleteSectionElement(pointToBeDeleted, false);
					}

					#endregion
				}
				else if (section.SectionType.Name.Equals("MainDeck") ||
				  section.SectionType.Name.Equals("OtherDecks") ||
				  section.SectionType.Name.Equals("InnerBottom") ||
				  section.SectionType.Name.Equals("Bottom")
				 )
				{
					#region Decks and Inner Bottom

					#region Old Code

					//// Get plates in campaigns of construction or in campaigns of conversion
					//SortedList<int, Plate> originalPlates = new SortedList<int, Plate>();

					//List<Campaign> originalCampaigns = vessel.GetOriginalCampaigns();
					//foreach (Campaign campaign in originalCampaigns)
					//{
					//    SortedList<string, IList> platesInCampaign = section.GetElementsInCampaign(campaign, true, "Plate");
					//    if (platesInCampaign.ContainsKey("Plate"))
					//    {
					//        List<Plate> platesInCampaignList = platesInCampaign["Plate"].Cast<Plate>().ToList();
					//        foreach (Plate plate in platesInCampaignList)
					//        {
					//            originalPlates.Add(plate.ID, plate);
					//        }
					//    }
					//}

					//// Get plates in center column
					//Plane centerPlaneYZ = new Plane(new Vector3D(1, 0, 0), section.BoundingBox.Center);
					//SortedList<int, Plate> intersectedPlatesYZ = this.IntersectPlates(originalPlates, centerPlaneYZ);

					//// Get bounding box of plates in center column
					//BoundingBox boundingBox = new BoundingBox();
					//foreach (Plate plate in intersectedPlatesYZ.Values)
					//{
					//    boundingBox.Union(plate.Design.Geometry.BoundingBox);
					//}

					//Plane centerPlaneXZ = new Plane(new Vector3D(0, 1, 0), boundingBox.Center);

					//Vector3D xAxis = new Vector3D(1, 0, 0);
					//SortedList<double, Compartment> sortedCompartments = this.IntersectCompartments(section, centerPlaneXZ);
					//foreach (Compartment compartment in sortedCompartments.Values)
					//{
					//    Plane minPlane = new Plane(xAxis, compartment.BoundingBox.MinPoint);
					//    Plane maxPlane = new Plane(xAxis, compartment.BoundingBox.MaxPoint);
					//    List<SteelThicknessGaugingPoint> pointsBetweenPlanes = this.GetPointsBetweenPlanes(steelThicknessGaugingPoints, minPlane, maxPlane);

					#endregion

					SortedList<float, SteelThicknessGaugingPoint> sortedSteelThicknessGaugingPoints = new SortedList<float, SteelThicknessGaugingPoint>();

					foreach (SteelThicknessGaugingPoint steelThicknessGaugingPoint in steelThicknessGaugingPoints)
					{
						float x = steelThicknessGaugingPoint.PointGeometry.Position.X;
						while (sortedSteelThicknessGaugingPoints.ContainsKey(x))
						{
							x += (float)Point3D.Epsilon;
						}

						sortedSteelThicknessGaugingPoints.Add(x, steelThicknessGaugingPoint);
					}

					float minKey = sortedSteelThicknessGaugingPoints.Keys[0];
					float maxKey = sortedSteelThicknessGaugingPoints.Keys[sortedSteelThicknessGaugingPoints.Count - 1];

					Point3D minPoint = sortedSteelThicknessGaugingPoints[minKey].PointGeometry.Position;
					Point3D maxPoint = sortedSteelThicknessGaugingPoints[maxKey].PointGeometry.Position;

					minPoint.Translate(-2 * Point3D.Epsilon, 0, 0);
					maxPoint.Translate(2 * Point3D.Epsilon, 0, 0);

					double defaultSketchSize = 40;
					double sizeX = maxPoint.X - minPoint.X;

					int numberOfSketches = (int)Math.Floor(sizeX / defaultSketchSize);

					if (sizeX < defaultSketchSize)
					{
						defaultSketchSize = sizeX;
						numberOfSketches = 1;
					}
					else
					{
						double mod = sizeX - (numberOfSketches * defaultSketchSize);
						if (mod > (defaultSketchSize / 2))
						{
							numberOfSketches++;
							defaultSketchSize -= (mod / numberOfSketches);
						}
						else
						{
							defaultSketchSize += (mod / numberOfSketches);
						}
					}

					Vector3D planeNormal = new Vector3D(1, 0, 0);
					for (int i = 0; i < numberOfSketches; i++)
					{
						Point3D minPlanePoint = new Point3D(minPoint.X + (i * defaultSketchSize), minPoint.Y, minPoint.Z);
						Point3D maxPlanePoint = new Point3D(minPoint.X + ((i + 1) * defaultSketchSize), minPoint.Y, minPoint.Z);
						Plane minPlane = new Plane(planeNormal, minPlanePoint);
						Plane maxPlane = new Plane(planeNormal, maxPlanePoint);
						List<SteelThicknessGaugingPoint> pointsBetweenPlanes = this.GetPointsBetweenPlanes(steelThicknessGaugingPoints, minPlane, maxPlane);

						if (pointsBetweenPlanes == null || pointsBetweenPlanes.Count == 0)
						{
							continue;
						}

						pointsPerSketch.Add(index, pointsBetweenPlanes);
						index++;

						foreach (SteelThicknessGaugingPoint pointBetweenPlane in pointsBetweenPlanes)
						{
							if (steelThicknessGaugingPoints.Contains(pointBetweenPlane))
							{
								steelThicknessGaugingPoints.Remove(pointBetweenPlane);
							}
						}

						//List<SectionElement> sectionElements = this.GetSectionElementsVisibleInSketch(section, compartment);
						List<SectionElement> sectionElements = this.GetSectionElementsVisibleInSketch(section, minPlane, maxPlane);

						// Generate thumbnail
						section.SectionDrawer.ConfigureView(this.glPanel, section.GetDefaultView(), true);
						section.SectionDrawer.UpdateLastCamera(this.glPanel);

						Bitmap thumbnail = originalThumbnail.Clone() as Bitmap;

						Rectangle rectangle = this.GetRectangleOfRenderizedArea(sectionElements);
						for (int j = 0; j < thumbnail.Width; j++)
						{
							for (int k = 0; k < thumbnail.Height; k++)
							{
								if (this.IsPixelOfRectangle(j, k, rectangle, 3))
								{
									thumbnail.SetPixel(j, k, Color.Red);
								}
							}
						}

						//int newWidht = (int)(thumbnail.Width / 5);
						//int newHeight = (int)(thumbnail.Height / 5);
						//Image image = thumbnail.GetThumbnailImage(newWidht, newHeight, null, IntPtr.Zero);
						//thumbnail = new Bitmap(image);

						string thumbName = thumbnail.RawFormat.Guid.ToString() + "-" + this.bitmapCount.ToString() + ".jpg";
						this.bitmapCount++;

						thumbName = document.HDF5DataAccessController.SaveBitmap(thumbnail, thumbName);
						bitmaps.Add(thumbName);

						C4DView view = section.SectionDrawer.ComputeViewForSectionElements(this.glPanel, sectionElements, false, 1.2f, true);
						if (view == null)
						{
							view = section.GetDefaultView();
						}

						section.SectionDrawer.ConfigureView(this.glPanel, view, false);
						section.SectionDrawer.UpdateLastCamera(this.glPanel);

						List<int> pointIDs = new List<int>();
						foreach (SteelThicknessGaugingPoint steelThicknessGaugingPoint in pointsBetweenPlanes)
						{
							pointIDs.Add(steelThicknessGaugingPoint.ID);
						}

						section.SectionDrawer.SetPriorityVisibleElements("SteelThicknessGaugingPoint", pointIDs);

						Bitmap bitmap = section.SectionDrawer.DrawModelImmediateModeCuttingOfCompartment(this.glPanel);

						section.SectionDrawer.ClearPriorityVisibleElements("SteelThicknessGaugingPoint");

						string bitmapName = bitmap.RawFormat.Guid.ToString() + "-" + this.bitmapCount.ToString() + ".jpg";
						this.bitmapCount++;

						bitmapName = document.HDF5DataAccessController.SaveBitmap(bitmap, bitmapName);
						bitmaps.Add(bitmapName);

						bitmap.Dispose();
					}

					#endregion
				}
				else if (section.SectionType.Name.Equals("Shell") ||
						 section.SectionType.Name.Equals("Longitudinal") ||
						 section.SectionType.Name.Equals("LongitudinalBulkheads") ||
						 section.SectionType.Name.Equals("LongitudinalCentralBulkheads")
				 )
				{
					#region Symmetric Longitudinal

					#region Old Code

					//int numberOfSketches = 9;

					//BoundingBox sectionBoundingBox = section.GetBoundingBoxOfElementType("Plate");

					//Point3D minPoint = sectionBoundingBox.MinPoint;
					//Point3D maxPoint = sectionBoundingBox.MaxPoint;

					//double sizeX = maxPoint.X - minPoint.X;
					//double deltaX = Math.Ceiling(sizeX / numberOfSketches);

					//Vector3D planeNormal = new Vector3D(1, 0, 0);
					//for (int i = 0; i < numberOfSketches; i++)
					//{
					//    Point3D minPlanePoint = new Point3D(minPoint.X + (i * deltaX), minPoint.Y, minPoint.Z);
					//    Point3D maxPlanePoint = new Point3D(minPoint.X + ((i + 1) * deltaX), minPoint.Y, minPoint.Z);
					//    Plane minPlane = new Plane(planeNormal, minPlanePoint);
					//    Plane maxPlane = new Plane(planeNormal, maxPlanePoint);

					#endregion

					SortedList<float, SteelThicknessGaugingPoint> sortedSteelThicknessGaugingPoints = new SortedList<float, SteelThicknessGaugingPoint>();

					foreach (SteelThicknessGaugingPoint steelThicknessGaugingPoint in steelThicknessGaugingPoints)
					{
						float x = steelThicknessGaugingPoint.PointGeometry.Position.X;
						while (sortedSteelThicknessGaugingPoints.ContainsKey(x))
						{
							x += (float)Point3D.Epsilon;
						}

						sortedSteelThicknessGaugingPoints.Add(x, steelThicknessGaugingPoint);
					}

					float minKey = sortedSteelThicknessGaugingPoints.Keys[0];
					float maxKey = sortedSteelThicknessGaugingPoints.Keys[sortedSteelThicknessGaugingPoints.Count - 1];

					Point3D minPoint = sortedSteelThicknessGaugingPoints[minKey].PointGeometry.Position;
					Point3D maxPoint = sortedSteelThicknessGaugingPoints[maxKey].PointGeometry.Position;

					minPoint.Translate(-2 * Point3D.Epsilon, 0, 0);
					maxPoint.Translate(2 * Point3D.Epsilon, 0, 0);

					double defaultSketchSize = 40;
					double sizeX = maxPoint.X - minPoint.X;

					int numberOfSketches = (int)Math.Floor(sizeX / defaultSketchSize);

					if (sizeX < defaultSketchSize)
					{
						defaultSketchSize = sizeX;
						numberOfSketches = 1;
					}
					else
					{
						double mod = sizeX - (numberOfSketches * defaultSketchSize);
						if (mod > (defaultSketchSize / 2))
						{
							numberOfSketches++;
							defaultSketchSize -= (mod / numberOfSketches);
						}
						else
						{
							defaultSketchSize += (mod / numberOfSketches);
						}
					}

					Vector3D planeNormal = new Vector3D(1, 0, 0);
					for (int i = 0; i < numberOfSketches; i++)
					{
						Point3D minPlanePoint = new Point3D(minPoint.X + (i * defaultSketchSize), minPoint.Y, minPoint.Z);
						Point3D maxPlanePoint = new Point3D(minPoint.X + ((i + 1) * defaultSketchSize), minPoint.Y, minPoint.Z);
						Plane minPlane = new Plane(planeNormal, minPlanePoint);
						Plane maxPlane = new Plane(planeNormal, maxPlanePoint);

						List<SteelThicknessGaugingPoint> gaugingPointsBySketch = this.GetPointsBetweenPlanes(steelThicknessGaugingPoints, minPlane, maxPlane);
						if (gaugingPointsBySketch == null || gaugingPointsBySketch.Count == 0)
						{
							continue;
						}

						pointsPerSketch.Add(index, gaugingPointsBySketch);
						index++;

						Vector3D sketchNormal = new Vector3D();
						foreach (SteelThicknessGaugingPoint gaugingPointBySketch in gaugingPointsBySketch)
						{
							sketchNormal += gaugingPointBySketch.PointGeometry.Normal;

							if (steelThicknessGaugingPoints.Contains(gaugingPointBySketch))
							{
								steelThicknessGaugingPoints.Remove(gaugingPointBySketch);
							}
						}
						sketchNormal /= gaugingPointsBySketch.Count;
						sketchNormal.Normalize();

						List<SectionElement> sectionElements = this.GetSectionElementsVisibleInSketch(section, minPlane, maxPlane, gaugingPointsBySketch, sketchNormal);

						// Generate thumbnail
						section.SectionDrawer.ConfigureView(this.glPanel, section.GetDefaultView(), true);
						section.SectionDrawer.UpdateLastCamera(this.glPanel);

						Bitmap thumbnail = originalThumbnail.Clone() as Bitmap;

						Rectangle rectangle = this.GetRectangleOfRenderizedArea(sectionElements);
						for (int j = 0; j < thumbnail.Width; j++)
						{
							for (int k = 0; k < thumbnail.Height; k++)
							{
								if (this.IsPixelOfRectangle(j, k, rectangle, 3))
								{
									thumbnail.SetPixel(j, k, Color.Red);
								}
							}
						}

						string thumbName = thumbnail.RawFormat.Guid.ToString() + "-" + this.bitmapCount.ToString() + ".jpg";
						this.bitmapCount++;

						thumbName = document.HDF5DataAccessController.SaveBitmap(thumbnail, thumbName);
						bitmaps.Add(thumbName);

						C4DView view = section.SectionDrawer.ComputeViewForSectionElements(this.glPanel, sectionElements, false, 1.2f, true);
						if (view == null)
						{
							view = section.GetDefaultView();
						}

						section.SectionDrawer.ConfigureView(this.glPanel, view, false);
						section.SectionDrawer.UpdateLastCamera(this.glPanel);

						List<int> pointIDs = new List<int>();
						foreach (SteelThicknessGaugingPoint steelThicknessGaugingPoint in gaugingPointsBySketch)
						{
							pointIDs.Add(steelThicknessGaugingPoint.ID);
						}

						section.SectionDrawer.SetPriorityVisibleElements("SteelThicknessGaugingPoint", pointIDs);

						Bitmap bitmap = section.SectionDrawer.DrawModelImmediateModeCuttingOfCompartment(this.glPanel);

						section.SectionDrawer.ClearPriorityVisibleElements("SteelThicknessGaugingPoint");

						string bitmapName = bitmap.RawFormat.Guid.ToString() + "-" + this.bitmapCount.ToString() + ".jpg";
						this.bitmapCount++;

						bitmapName = document.HDF5DataAccessController.SaveBitmap(bitmap, bitmapName);
						bitmaps.Add(bitmapName);

						bitmap.Dispose();
					}

					#endregion
				}
				else if (section.SectionType.Name.Equals("AnyOtherSection"))
				{
					#region Any Other Section

					pointsPerSketch.Add(index, steelThicknessGaugingPoints);
					index++;

					List<SectionElement> sectionElements = this.GetSectionElementsVisibleInSketch(section, steelThicknessGaugingPoints);

					Bitmap thumbnail = originalThumbnail.Clone() as Bitmap;

					Rectangle rectangle = this.GetRectangleOfRenderizedArea(sectionElements);
					for (int i = 0; i < thumbnail.Width; i++)
					{
						for (int j = 0; j < thumbnail.Height; j++)
						{
							if (this.IsPixelOfRectangle(i, j, rectangle, 3))
							{
								thumbnail.SetPixel(i, j, Color.Red);
							}
						}
					}

					//int newWidht = (int)(thumbnail.Width / 5);
					//int newHeight = (int)(thumbnail.Height / 5);
					//Image image = thumbnail.GetThumbnailImage(newWidht, newHeight, null, IntPtr.Zero);
					//thumbnail = new Bitmap(image);

					string thumbName = thumbnail.RawFormat.Guid.ToString() + "-" + this.bitmapCount.ToString() + ".jpg";
					this.bitmapCount++;

					thumbName = document.HDF5DataAccessController.SaveBitmap(thumbnail, thumbName);
					bitmaps.Add(thumbName);

					//section.SectionDrawer.CurrentViewModeName = "SketchFormat";

					C4DView view = section.SectionDrawer.ComputeViewForSectionElements(this.glPanel, sectionElements, false);
					if (view == null)
					{
						view = section.GetDefaultView();
					}

					section.SectionDrawer.ConfigureView(this.glPanel, view, false);
					section.SectionDrawer.UpdateLastCamera(this.glPanel);

					// Get IDs of gauging points to set in SectionDrawer
					List<int> pointIDs = new List<int>();
					foreach (SteelThicknessGaugingPoint steelThicknessGaugingPoint in steelThicknessGaugingPoints)
					{
						pointIDs.Add(steelThicknessGaugingPoint.ID);
					}

					section.SectionDrawer.SetPriorityVisibleElements("SteelThicknessGaugingPoint", pointIDs);

					Bitmap bitmap = section.SectionDrawer.DrawModelImmediateModeCuttingOfCompartment(this.glPanel);

					section.SectionDrawer.ClearPriorityVisibleElements("SteelThicknessGaugingPoint");

					string bitmapName = bitmap.RawFormat.Guid.ToString() + "-" + this.bitmapCount.ToString() + ".jpg";
					this.bitmapCount++;

					bitmapName = document.HDF5DataAccessController.SaveBitmap(bitmap, bitmapName);
					bitmaps.Add(bitmapName);

					bitmap.Dispose();

					#endregion
				}

				section.SectionDrawer.CurrentViewModeName = sectionDrawerCurrentViewModeName;

				return bitmaps;
			}
			catch (Exception ex)
			{
				throw new ApplicationException("Sketch for section/campaign report cannot be created!", ex);
			}
		}

		public List<string> CreateSketch(Section section, Section symmetricSection, Campaign campaign, List<SteelThicknessGaugingPoint> steelThicknessGaugingPoints, out SortedList<int, List<SteelThicknessGaugingPoint>> pointsPerSketch)
		{
			int index = 1;
			List<string> bitmaps = new List<string>();
			SortedList<int, List<SteelThicknessGaugingPoint>> campaignReportPointsPerSketch;

			pointsPerSketch = new SortedList<int, List<SteelThicknessGaugingPoint>>();

			foreach (CampaignReport campaignReport in campaign.CampaignReports.Values)
			{
				List<string> campaignReportBitmaps = CreateSketch(section, symmetricSection, campaignReport, steelThicknessGaugingPoints, out campaignReportPointsPerSketch);
				if (campaignReportBitmaps != null && campaignReportBitmaps.Count > 0)
				{
					bitmaps.AddRange(campaignReportBitmaps);

					foreach (List<SteelThicknessGaugingPoint> points in campaignReportPointsPerSketch.Values)
					{
						pointsPerSketch.Add(index, points);
						index++;
					}
				}
			}

			return bitmaps;
		}
		public List<string> CreateSketch(Section section, Section symmetricSection, CampaignReport campaignReport, List<SteelThicknessGaugingPoint> steelThicknessGaugingPoints, out SortedList<int, List<SteelThicknessGaugingPoint>> pointsPerSketch)
		{
			try
			{
				if (section == null)
				{
					throw new ArgumentNullException("section");
				}

				if (symmetricSection == null)
				{
					throw new ArgumentNullException("symmetricSection");
				}

				if (campaignReport == null)
				{
					throw new ArgumentNullException("campaignReport");
				}

				int index = 0;
				pointsPerSketch = new SortedList<int, List<SteelThicknessGaugingPoint>>();

				if (!section.SectionType.Name.Equals("Shell") && !section.SectionType.Name.Equals("Longitudinal") &&
					!section.SectionType.Name.Equals("LongitudinalBulkheads") && !symmetricSection.SectionType.Name.Equals("Shell") &&
					!symmetricSection.SectionType.Name.Equals("Longitudinal") && !symmetricSection.SectionType.Name.Equals("LongitudinalBulkheads")
					)
				{
					return null;
				}

				string sectionDrawerCurrentViewModeName = section.SectionDrawer.CurrentViewModeName;
				section.SectionDrawer.CurrentViewModeName = "SketchFormat";

				Vessel vessel = section.Vessel;
				Document document = vessel.Document as Document;

				if (!this.sectionThumbnails.ContainsKey(vessel.GUID))
				{
					this.sectionThumbnails.Add(vessel.GUID, new Dictionary<int, Bitmap>());
				}
				if (!this.sectionThumbnails[vessel.GUID].ContainsKey(section.ID))
				{
					this.sectionThumbnails[vessel.GUID].Add(section.ID, null);
				}
				if (this.sectionThumbnails[vessel.GUID][section.ID] == null)
				{
					CaptionsModel.Instance.SetCaptionPropertySelection("Plate", "IACSSketchDescription", false);

					section.SectionDrawer.CurrentViewModeName = "Wireframe";

					section.SectionDrawer.ConfigureView(this.glPanel, section.GetDefaultView(), true);
					section.SectionDrawer.UpdateLastCamera(this.glPanel);


					section.SectionDrawer.DrawAxis = false;
					bool isWireframeVisible = ViewModesData.Instance.IsVisible(section.SectionDrawer.CurrentViewModeName, "Wireframe");
					if (!isWireframeVisible)
					{
						ViewModesData.Instance.SetVisibility(section.SectionDrawer.CurrentViewModeName, "Wireframe", true, true);
					}

					Bitmap bitmap = section.SectionDrawer.DrawModelImmediateMode(this.glPanel);

					if (!isWireframeVisible)
					{
						ViewModesData.Instance.SetVisibility(section.SectionDrawer.CurrentViewModeName, "Wireframe", false, true);
					}

					section.SectionDrawer.DrawAxis = true;

					this.sectionThumbnails[vessel.GUID][section.ID] = bitmap;

					section.SectionDrawer.CurrentViewModeName = "SketchFormat";

					CaptionsModel.Instance.SetCaptionPropertySelection("Plate", "IACSSketchDescription", true);
				}

				Bitmap originalThumbnail = this.sectionThumbnails[vessel.GUID][section.ID];

				List<string> bitmaps = new List<string>();

				List<SteelThicknessGaugingPoint> sectionSteelThicknessGaugingPoints = new List<SteelThicknessGaugingPoint>();
				List<SteelThicknessGaugingPoint> symmetricSteelThicknessGaugingPoints = new List<SteelThicknessGaugingPoint>();

				foreach (SteelThicknessGaugingPoint steelThicknessGaugingPoint in steelThicknessGaugingPoints)
				{
					if (steelThicknessGaugingPoint.Section.ID == section.ID)
					{
						sectionSteelThicknessGaugingPoints.Add(steelThicknessGaugingPoint);
					}
					else if (steelThicknessGaugingPoint.Section.ID == symmetricSection.ID)
					{
						symmetricSteelThicknessGaugingPoints.Add(steelThicknessGaugingPoint);
					}
				}

				if (sectionSteelThicknessGaugingPoints != null || symmetricSteelThicknessGaugingPoints != null)
				{
					#region Old Code

					//int numberOfSketches = 9;

					//Point3D minPoint = section.BoundingBox.MinPoint;
					//Point3D maxPoint = section.BoundingBox.MaxPoint;

					//double sizeX = maxPoint.X - minPoint.X;
					//double deltaX = Math.Ceiling(sizeX / numberOfSketches);

					//Vector3D planeNormal = new Vector3D(1, 0, 0);

					//for (int i = 0; i < numberOfSketches; i++)
					//{
					//    Point3D minPlanePoint = new Point3D(minPoint.X + (i * deltaX), minPoint.Y, minPoint.Z);
					//    Point3D maxPlanePoint = new Point3D(minPoint.X + ((i + 1) * deltaX), minPoint.Y, minPoint.Z);
					//    Plane minPlane = new Plane(planeNormal, minPlanePoint);
					//    Plane maxPlane = new Plane(planeNormal, maxPlanePoint);

					#endregion

					SortedList<float, SteelThicknessGaugingPoint> sortedSteelThicknessGaugingPoints = new SortedList<float, SteelThicknessGaugingPoint>();

					foreach (SteelThicknessGaugingPoint steelThicknessGaugingPoint in steelThicknessGaugingPoints)
					{
						float x = steelThicknessGaugingPoint.PointGeometry.Position.X;
						while (sortedSteelThicknessGaugingPoints.ContainsKey(x))
						{
							x += (float)Point3D.Epsilon;
						}

						sortedSteelThicknessGaugingPoints.Add(x, steelThicknessGaugingPoint);
					}

					float minKey = sortedSteelThicknessGaugingPoints.Keys[0];
					float maxKey = sortedSteelThicknessGaugingPoints.Keys[sortedSteelThicknessGaugingPoints.Count - 1];

					Point3D minPoint = sortedSteelThicknessGaugingPoints[minKey].PointGeometry.Position;
					Point3D maxPoint = sortedSteelThicknessGaugingPoints[maxKey].PointGeometry.Position;

					minPoint.Translate(-2 * Point3D.Epsilon, 0, 0);
					maxPoint.Translate(2 * Point3D.Epsilon, 0, 0);

					double defaultSketchSize = 40;
					double sizeX = maxPoint.X - minPoint.X;

					int numberOfSketches = (int)Math.Floor(sizeX / defaultSketchSize);

					if (sizeX < defaultSketchSize)
					{
						defaultSketchSize = sizeX;
						numberOfSketches = 1;
					}
					else
					{
						double mod = sizeX - (numberOfSketches * defaultSketchSize);
						if (mod > (defaultSketchSize / 2))
						{
							numberOfSketches++;
							defaultSketchSize -= (mod / numberOfSketches);
						}
						else
						{
							defaultSketchSize += (mod / numberOfSketches);
						}
					}

					Vector3D planeNormal = new Vector3D(1, 0, 0);
					for (int i = 0; i < numberOfSketches; i++)
					{
						Point3D minPlanePoint = new Point3D(minPoint.X + (i * defaultSketchSize), minPoint.Y, minPoint.Z);
						Point3D maxPlanePoint = new Point3D(minPoint.X + ((i + 1) * defaultSketchSize), minPoint.Y, minPoint.Z);
						Plane minPlane = new Plane(planeNormal, minPlanePoint);
						Plane maxPlane = new Plane(planeNormal, maxPlanePoint);

						List<SteelThicknessGaugingPoint> sectionGaugingPointsBySketch = (sectionSteelThicknessGaugingPoints != null) ? this.GetPointsBetweenPlanes(sectionSteelThicknessGaugingPoints, minPlane, maxPlane) : null;
						List<SteelThicknessGaugingPoint> symmetricGaugingPointsBySketch = (symmetricSteelThicknessGaugingPoints != null) ? this.GetPointsBetweenPlanes(symmetricSteelThicknessGaugingPoints, minPlane, maxPlane) : null;

						if ((sectionGaugingPointsBySketch == null || sectionGaugingPointsBySketch.Count == 0) &&
							(symmetricGaugingPointsBySketch == null || symmetricGaugingPointsBySketch.Count == 0))
						{
							continue;
						}

						List<SteelThicknessGaugingPoint> gaugingPointsBySketch = new List<SteelThicknessGaugingPoint>();
						if (sectionGaugingPointsBySketch != null && sectionGaugingPointsBySketch.Count > 0)
						{
							gaugingPointsBySketch.AddRange(sectionGaugingPointsBySketch);
						}
						if (symmetricGaugingPointsBySketch != null && symmetricGaugingPointsBySketch.Count > 0)
						{
							gaugingPointsBySketch.AddRange(symmetricGaugingPointsBySketch);
						}

						pointsPerSketch.Add(index, gaugingPointsBySketch);
						index++;

						#region Compute gauging points in symmetric section

						List<SteelThicknessGaugingPoint> pointsToBeDeleted = new List<SteelThicknessGaugingPoint>();

						if (symmetricGaugingPointsBySketch != null && symmetricGaugingPointsBySketch.Count > 0)
						{
							#region Clone gauging points only in SB side, to PS side to be renderized in sketch

							#region Find center plane parallel to X and Z axis

							BoundingBox sectionBoundingBox = new BoundingBox();
							BoundingBox symmetricBoundingBox = new BoundingBox();

							List<Campaign> originalCampaigns = vessel.GetOriginalCampaigns();
							foreach (Campaign campaign in originalCampaigns)
							{
								IList platesInCampaign = section.GetPermanentElementsInCampaign(campaign, true, "Plate");
								if (platesInCampaign != null && platesInCampaign.Count > 0)
								{
									foreach (Plate plate in platesInCampaign)
									{
										sectionBoundingBox.Union(plate.Design.Geometry.BoundingBox);
									}
								}

								platesInCampaign = symmetricSection.GetPermanentElementsInCampaign(campaign, true, "Plate");
								if (platesInCampaign != null && platesInCampaign.Count > 0)
								{
									foreach (Plate plate in platesInCampaign)
									{
										symmetricBoundingBox.Union(plate.Design.Geometry.BoundingBox);
									}
								}
							}

							BoundingBox boundingBox = new BoundingBox(sectionBoundingBox);
							boundingBox.Union(symmetricBoundingBox);

							Plane plane = new Plane(new Vector3D(0, 1, 0), boundingBox.Center);

							#endregion

							SortedList<int, List<SteelThicknessGaugingPoint>> sectionOrganizedGaugingPoints = this.OrganizeIACSGaugingPointsByID(sectionGaugingPointsBySketch);
							foreach (SteelThicknessGaugingPoint symmetricGaugingPoint in symmetricGaugingPointsBySketch)
							{
								if (sectionOrganizedGaugingPoints.ContainsKey((int)symmetricGaugingPoint.IACSReportLocationData.ID))
								{
									bool haveSymmetricPoint = false;
									foreach (SteelThicknessGaugingPoint symmetricOrganizedGaugingPoint in sectionOrganizedGaugingPoints[(int)symmetricGaugingPoint.IACSReportLocationData.ID])
									{
										if (symmetricOrganizedGaugingPoint.IACSReportLocationData.IsForward == symmetricGaugingPoint.IACSReportLocationData.IsForward)
										{
											haveSymmetricPoint = true;
											break;
										}
									}

									if (haveSymmetricPoint)
									{
										continue;
									}
								}

								Point3D pointPosition = new Point3D(symmetricGaugingPoint.PointGeometry.Position);
								pointPosition = (Point3D)pointPosition.Mirror(plane);
								Vector3D pointNormal = new Vector3D(symmetricGaugingPoint.PointGeometry.Normal * -1);

								int geometryID, geometryPartID;
								List<string> elementTypeNames = new List<string>();
								elementTypeNames.Add(symmetricGaugingPoint.TargetElement.GetBaseType().Name);
								Point3D closestPoint;
								SectionElement sectionElement = section.PointOverDomainEntity(pointPosition, elementTypeNames, 0.01, out closestPoint, out geometryID, out geometryPartID) as SectionElement;
								if (sectionElement == null)
								{
									continue;
								}

								AverageSteelThicknessGaugingPoint averageSteelThicknessGaugingPoint = new AverageSteelThicknessGaugingPoint();

								averageSteelThicknessGaugingPoint.PointGeometry.Position = pointPosition;
								averageSteelThicknessGaugingPoint.PointGeometry.Normal = pointNormal;

								averageSteelThicknessGaugingPoint.Section = section;
								averageSteelThicknessGaugingPoint.Campaign = campaignReport.Campaign;
								averageSteelThicknessGaugingPoint.CampaignReport = campaignReport;
								averageSteelThicknessGaugingPoint.TargetElement = sectionElement as IWorkableElement;
								averageSteelThicknessGaugingPoint.TargetElementGeometryID = geometryID;
								averageSteelThicknessGaugingPoint.TargetElementGeometrySegmentID = geometryPartID;
								averageSteelThicknessGaugingPoint.TargetElementPart = symmetricGaugingPoint.TargetElementPart;
								averageSteelThicknessGaugingPoint.Active = true;

								averageSteelThicknessGaugingPoint.IACSReportLocationData.ID = symmetricGaugingPoint.IACSReportLocationData.ID;
								averageSteelThicknessGaugingPoint.IACSReportLocationData.SetColumnNull();
								averageSteelThicknessGaugingPoint.IACSReportLocationData.Strake = symmetricGaugingPoint.IACSReportLocationData.Strake;
								averageSteelThicknessGaugingPoint.IACSReportLocationData.IsForward = (bool)symmetricGaugingPoint.IACSReportLocationData.IsForward;
								averageSteelThicknessGaugingPoint.IACSReportLocationData.IsPortSide = !(bool)symmetricGaugingPoint.IACSReportLocationData.IsPortSide;

								//#if (DEBUG)
								//newFrontGaugingPoint.Name = ((bool)newFrontGaugingPoint.IACSReportLocationData.IsForward) ? "F " : "B ";
								//newFrontGaugingPoint.Name += newFrontGaugingPoint.IACSReportLocationData.ID.ToString();
								//newFrontGaugingPoint.Name += ((bool)newFrontGaugingPoint.IACSReportLocationData.IsPortSide) ? " P " : " S ";
								//#else
								averageSteelThicknessGaugingPoint.Name = averageSteelThicknessGaugingPoint.IACSReportLocationData.ID.ToString();
								//#endif
								section.AddPermanentDomainEntityToGroup(averageSteelThicknessGaugingPoint, true, false);
								//section.Vessel.Document.DocumentStateController.BroadcastEntityCreation(this, averageSteelThicknessGaugingPoint);

								sectionGaugingPointsBySketch.Add(averageSteelThicknessGaugingPoint);
								pointsToBeDeleted.Add(averageSteelThicknessGaugingPoint);
							}

							#endregion
						}

						#endregion

						#region Remove computed gauging points of collections to avoid unnecessary computations

						int sketchPointsCount = 0;
						Vector3D sketchNormal = new Vector3D();

						if (sectionGaugingPointsBySketch != null)
						{
							foreach (SteelThicknessGaugingPoint sectionGaugingPointBySketch in sectionGaugingPointsBySketch)
							{
								sketchNormal += sectionGaugingPointBySketch.PointGeometry.Normal;

								if (sectionSteelThicknessGaugingPoints.Contains(sectionGaugingPointBySketch))
								{
									sectionSteelThicknessGaugingPoints.Remove(sectionGaugingPointBySketch);
								}
							}

							sketchPointsCount += sectionGaugingPointsBySketch.Count;
						}

						if (symmetricGaugingPointsBySketch != null)
						{
							foreach (SteelThicknessGaugingPoint symmetricGaugingPointBySketch in symmetricGaugingPointsBySketch)
							{
								sketchNormal += symmetricGaugingPointBySketch.PointGeometry.Normal;

								if (symmetricSteelThicknessGaugingPoints.Contains(symmetricGaugingPointBySketch))
								{
									symmetricSteelThicknessGaugingPoints.Remove(symmetricGaugingPointBySketch);
								}
							}

							sketchPointsCount += symmetricGaugingPointsBySketch.Count;
						}

						sketchNormal /= sketchPointsCount;
						sketchNormal.Normalize();

						#endregion

						if (sectionGaugingPointsBySketch.Count > 0)
						{
							List<SectionElement> sectionElements = this.GetSectionElementsVisibleInSketch(section, minPlane, maxPlane, sectionGaugingPointsBySketch, sketchNormal);

							// Generate thumbnail
							section.SectionDrawer.ConfigureView(this.glPanel, section.GetDefaultView(), true);
							section.SectionDrawer.UpdateLastCamera(this.glPanel);

							Bitmap thumbnail = new Bitmap(originalThumbnail, originalThumbnail.Width, originalThumbnail.Height);

							Rectangle rectangle = this.GetRectangleOfRenderizedArea(sectionElements);
							for (int j = 0; j < thumbnail.Width; j++)
							{
								for (int k = 0; k < thumbnail.Height; k++)
								{
									if (this.IsPixelOfRectangle(j, k, rectangle, 3))
									{
										thumbnail.SetPixel(j, k, Color.Red);
									}
								}
							}

							string thumbName = thumbnail.RawFormat.Guid.ToString() + "-" + this.bitmapCount.ToString() + ".jpg";
							this.bitmapCount++;

							thumbName = document.HDF5DataAccessController.SaveBitmap(thumbnail, thumbName);
							bitmaps.Add(thumbName);

							C4DView view = section.SectionDrawer.ComputeViewForSectionElements(this.glPanel, sectionElements, false, 1.2f, true);
							if (view == null)
							{
								view = section.GetDefaultView();
							}

							section.SectionDrawer.ConfigureView(this.glPanel, view, false);
							section.SectionDrawer.UpdateLastCamera(this.glPanel);

							List<int> gaugingPointIDs = new List<int>();
							foreach (SteelThicknessGaugingPoint steelThicknessGaugingPoint in sectionGaugingPointsBySketch)
							{
								gaugingPointIDs.Add(steelThicknessGaugingPoint.ID);
							}

							section.SectionDrawer.SetPriorityVisibleElements("SteelThicknessGaugingPoint", gaugingPointIDs);

							Bitmap bitmap = section.SectionDrawer.DrawModelImmediateModeCuttingOfCompartment(this.glPanel);

							section.SectionDrawer.ClearPriorityVisibleElements("SteelThicknessGaugingPoint");

							string bitmapName = bitmap.RawFormat.Guid.ToString() + "-" + this.bitmapCount.ToString() + ".jpg";
							this.bitmapCount++;

							bitmapName = document.HDF5DataAccessController.SaveBitmap(bitmap, bitmapName);
							bitmaps.Add(bitmapName);

							bitmap.Dispose();
						}

						foreach (SteelThicknessGaugingPoint pointToBeDeleted in pointsToBeDeleted)
						{
							document.DeleteSectionElement(pointToBeDeleted, false);
						}
					}
				}

				section.SectionDrawer.CurrentViewModeName = sectionDrawerCurrentViewModeName;

				return bitmaps;
			}
			catch (Exception ex)
			{
				throw new ApplicationException("Sketch for section/campaign report cannot be created!", ex);
			}
		}

		private GLPanel InitializeGLPanel(Document document)
		{
			GLPanel panel = new GLPanel();

			Size size = document.DocumentWindow.GLPanel.Size;
			double rectangleRatio = 1.0 * size.Width / size.Height;
			double screenRation = 1.0 * Screen.PrimaryScreen.Bounds.Width / Screen.PrimaryScreen.Bounds.Height;

			int width = Screen.PrimaryScreen.Bounds.Width - 10;
			int height = Screen.PrimaryScreen.Bounds.Height;

			if (screenRation > rectangleRatio)
			{
				width = Convert.ToInt32(height * rectangleRatio);
			}
			else if (screenRation < rectangleRatio)
			{
				height = Convert.ToInt32(width / rectangleRatio);
			}
			panel.Size = new Size(width, height);

			return panel;
		}

		private List<SectionElement> GetSectionElementsVisibleInSketch(Section section, List<SteelThicknessGaugingPoint> steelThicknessGaugingPoints)
		{
			try
			{
				Vector3D planeNormal = section.Normal * section.Up;
				List<SectionElement> sectionElements = new List<SectionElement>();

				if (section.SectionType.Name.Equals("WebFrames") ||
					section.SectionType.Name.Equals("TransverseBulkheads") ||
					section.SectionType.Name.Equals("TransverseCentralBulkheads") ||
					section.SectionType.Name.Equals("SwashBulkheads"))
				{
					BoundingBox boundingBox = section.GetBoundingBoxOfElementType("Plate");
					Plane plane = new Plane(planeNormal, boundingBox.Center);

					int pointsSignal = plane.Signal(steelThicknessGaugingPoints[0].PointGeometry.Position);
					pointsSignal = (pointsSignal == 1) ? pointsSignal : -1; // Avoid zero signal

					SectionElementsSet sectionElementsSet = section.GetPermanentSectionElementsSet("Plate");
					foreach (Plate plate in sectionElementsSet.Elements.Values)
					{
						int plateSignal = plane.Signal(plate.Design.Geometry.Primitive(0).Centroid);

						if (plateSignal == pointsSignal)
						{
							sectionElements.Add(plate);
						}
					}
				}
				//else if (section.SectionType.Name.Equals("MainDeck") ||
				//             section.SectionType.Name.Equals("OtherDecks") ||
				//             section.SectionType.Name.Equals("InnerBottom") ||
				//             section.SectionType.Name.Equals("Bottom")
				//            )
				//{
				//}
				//else if (section.SectionType.Name.Equals("Shell") ||
				//             section.SectionType.Name.Equals("Longitudinal") ||
				//             section.SectionType.Name.Equals("LongitudinalBulkheads") ||
				//             section.SectionType.Name.Equals("LongitudinalCentralBulkheads")
				//            )
				//{
				//}
				//else if (section.SectionType.Name.Equals("AnyOtherSection"))
				else
				{
					float xMin = float.MaxValue, xMax = float.MinValue;
					float yMin = float.MaxValue, yMax = float.MinValue;
					float zMin = float.MaxValue, zMax = float.MinValue;

					float increase = 0;
					BoundingBox sectionBoundingBox = section.GetBoundingBoxOfElementType("Plate");

					char[] sortAxisLabels = sectionBoundingBox.GetSizeOrderedAxisLabels();
					if (sortAxisLabels[0] == 'X')
					{
						increase = sectionBoundingBox.Width / 10;
					}
					else if (sortAxisLabels[0] == 'Y')
					{
						increase = sectionBoundingBox.Height / 10;
					}
					else if (sortAxisLabels[0] == 'Z')
					{
						increase = sectionBoundingBox.Depth / 10;
					}

					foreach (SteelThicknessGaugingPoint steelThicknessGaugingPoint in steelThicknessGaugingPoints)
					{
						Point3D point = steelThicknessGaugingPoint.PointGeometry.Position;

						xMin = ((point.X - increase) < xMin) ? (point.X - increase) : xMin;
						xMax = ((point.X + increase) > xMax) ? (point.X + increase) : xMax;

						yMin = ((point.Y - increase) < yMin) ? (point.Y - increase) : yMin;
						yMax = ((point.Y + increase) > yMax) ? (point.Y + increase) : yMax;

						zMin = ((point.Z - increase) < zMin) ? (point.Z - increase) : zMin;
						zMax = ((point.Z + increase) > zMax) ? (point.Z + increase) : zMax;
					}

					Point3D minPoint = new Point3D(xMin, yMin, zMin);
					Point3D maxPoint = new Point3D(xMax, yMax, zMax);

					Plane minPlane = new Plane(planeNormal, minPoint);
					Plane maxPlane = new Plane(planeNormal, maxPoint);

					return this.GetSectionElementsVisibleInSketch(section, minPlane, maxPlane);

					//SectionElementsSet sectionElementsSet = section.GetSectionElementsSet("Plate");
					//foreach (Plate plate in sectionElementsSet.Elements.Values)
					//{
					//    Point3D point = plate.Design.Geometry.Primitive(0).Centroid;
					//    if (minPlane.Signal(point) == 1 && maxPlane.Signal(point) == -1)
					//    {
					//        sectionElements.Add(plate);
					//    }
					//}
				}

				return sectionElements;
			}
			catch (Exception ex)
			{
				throw new ApplicationException("Section elements visible in skectch cannot be get!", ex);
			}
		}
		private List<SectionElement> GetSectionElementsVisibleInSketch(Section section, Compartment compartment)
		{
			try
			{
				List<SectionElement> sectionElements = new List<SectionElement>();

				Vector3D xAxis = new Vector3D(1, 0, 0);
				Plane minPlane = new Plane(xAxis, compartment.BoundingBox.MinPoint);
				Plane maxPlane = new Plane(xAxis, compartment.BoundingBox.MaxPoint);

				SectionElementsSet sectionElementsSet = section.GetPermanentSectionElementsSet("Plate");
				foreach (Plate plate in sectionElementsSet.Elements.Values)
				{
					Point3D point = plate.Design.Geometry.Primitive(0).Centroid;
					if (minPlane.Signal(point) == 1 && maxPlane.Signal(point) == -1)
					{
						sectionElements.Add(plate);
					}
				}

				return sectionElements;
			}
			catch (Exception ex)
			{
				throw new ApplicationException("Section elements visible in skectch cannot be get!", ex);
			}
		}
		private List<SectionElement> GetSectionElementsVisibleInSketch(Section section, Plane minPlane, Plane maxPlane)
		{
			try
			{
				List<SectionElement> sectionElements = new List<SectionElement>();

				SectionElementsSet sectionElementsSet = section.GetPermanentSectionElementsSet("Plate");
				foreach (Plate plate in sectionElementsSet.Elements.Values)
				{
					Point3D point = plate.Design.Geometry.Primitive(0).Centroid;
					if (minPlane.Signal(point) == 1 && maxPlane.Signal(point) == -1)
					{
						sectionElements.Add(plate);
					}
				}

				return sectionElements;
			}
			catch (Exception ex)
			{
				throw new ApplicationException("Section elements visible in skectch cannot be get!", ex);
			}
		}
		private List<SectionElement> GetSectionElementsVisibleInSketch(Section section, Plane minPlane, Plane maxPlane, List<SteelThicknessGaugingPoint> steelThicknessGaugingPoints, Vector3D normal)
		{
			try
			{
				SortedList<int, Plate> targetPlates = new SortedList<int, Plate>();
				foreach (SteelThicknessGaugingPoint steelThicknessGaugingPoint in steelThicknessGaugingPoints)
				{
					Plate targetPlate = steelThicknessGaugingPoint.TargetElement as Plate;
					if (targetPlate == null || targetPlates.ContainsKey(targetPlate.ID))
					{
						continue;
					}

					targetPlates.Add(targetPlate.ID, targetPlate);
				}

				List<SectionElement> sectionElements = new List<SectionElement>();

				SectionElementsSet sectionElementsSet = section.GetPermanentSectionElementsSet("Plate");
				foreach (Plate plate in sectionElementsSet.Elements.Values)
				{
					Point3D point = plate.Design.Geometry.Primitive(0).Centroid;

					Vector3D plateNormal = plate.Design.Geometry.Primitive(0).Normal;
					plateNormal.Normalize();

					double angle = Vector3D.Angle(plateNormal, normal);

					if ((minPlane.Signal(point) == 1 && maxPlane.Signal(point) == -1 && angle < 1.0) || targetPlates.ContainsKey(plate.ID))
					{
						sectionElements.Add(plate);
					}
				}

				return sectionElements;
			}
			catch (Exception ex)
			{
				throw new ApplicationException("Section elements visible in skectch cannot be get!", ex);
			}
		}

		private Rectangle GetRectangleOfRenderizedArea(List<SectionElement> sectionElements)
		{
			try
			{
				int expansion = 50;
				Rectangle rectangle = new Rectangle();

				BoundingBox boundingBox = new BoundingBox();
				foreach (SectionElement sectionElement in sectionElements)
				{
					boundingBox.Union(sectionElement.Design.Geometry.BoundingBox);
				}

				Point minPoint = this.glPanel.WorldToPixel(boundingBox.MinPoint);
				Point maxPoint = this.glPanel.WorldToPixel(boundingBox.MaxPoint);

				int minX = (minPoint.X < maxPoint.X) ? minPoint.X : maxPoint.X;
				int minY = (minPoint.Y < maxPoint.Y) ? minPoint.Y : maxPoint.Y;

				rectangle.Location = new Point(minX - expansion, minY - expansion);
				rectangle.Height = Math.Abs(minPoint.Y - maxPoint.Y) + 2 * expansion;
				rectangle.Width = Math.Abs(minPoint.X - maxPoint.X) + 2 * expansion;

				return rectangle;
			}
			catch (Exception ex)
			{
				throw new ApplicationException("Retangle of renderized area cannot be computed!", ex);
			}
		}
		private bool IsPixelOfRectangle(int x, int y, Rectangle rectangle, int lineWidth)
		{
			Point minPoint = rectangle.Location;
			Point maxPoint = new Point(minPoint.X + rectangle.Width, minPoint.Y + rectangle.Height);

			if (x >= minPoint.X - lineWidth && x <= minPoint.X + lineWidth && y >= minPoint.Y && y <= maxPoint.Y)
			{
				return true;
			}

			if (x >= maxPoint.X - lineWidth && x <= maxPoint.X + lineWidth && y >= minPoint.Y && y <= maxPoint.Y)
			{
				return true;
			}

			if (y >= minPoint.Y - lineWidth && y <= minPoint.Y + lineWidth && x >= minPoint.X && x <= maxPoint.X)
			{
				return true;
			}

			if (y >= maxPoint.Y - lineWidth && y <= maxPoint.Y + lineWidth && x >= minPoint.X && x <= maxPoint.X)
			{
				return true;
			}

			return false;
		}
	}
}
