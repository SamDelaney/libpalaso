#if MONO

#define DISABLE_KEYBOARDSWITCHING

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;

using Palaso.Code;
using Palaso.Reporting;
using Palaso.WritingSystems;

using IBusDotNet;
using Timer = System.Windows.Forms.Timer;

namespace Palaso.UI.WindowsForms.Keyboarding
{
		enum IBusEngineVersion
		{
			NotConfigured,
			V1,
			V2,
			V3,
			V4
		}
	internal class IBusAdaptor
	{
		static IBusConnection _connection;
		private static KeyboardController.KeyboardDescriptor _defaultKeyboard;
		private static Timer _timer;
		private static string _requestActivateName;
		private static IBusEngineVersion _engineVersion;

		enum IBusError
		{
			Unknown,
			ConnectionRefused,
			NotConfigured
		}

		/// <summary>
		/// Opens a _connection if one isn't already Opened.
		/// </summary>
		public static void EnsureConnection ()
		{
			OpenConnection();
			if (_connection != null)
			{
				foreach (var keyboard in GetKeyboardDescriptors())
				{
					_defaultKeyboard = keyboard;
					break;
				}
			}
		}

		public static void OpenConnection()
		{
#if !DISABLE_KEYBOARDSWITCHING
			if (_connection == null)
			{
				try
				{
					_connection = IBusConnectionFactory.Create();
					if (_connection == null)
					{
						NotifyUserOfProblem(IBusError.Unknown, "IBus doesn't seem to be running");
					}
					_engineVersion = IBusEngineVersion.NotConfigured;
				}
				catch (DirectoryNotFoundException e)
				{
					NotifyUserOfProblem(IBusError.NotConfigured, String.Format("Your keyboard cannot be changed automatically because IBus doesn't seem to installed (or configured).\n{0}", e.Message));
				}
				catch (Exception e)
				{
					if (e.Message.Contains("Connection refused"))
					{
						NotifyUserOfProblem(IBusError.ConnectionRefused, "IBus doesn't seem to be running");
					}
					else
					{
						throw;
					}
				}
			}
#endif
		}

		/// <summary>
		/// Close the connection. set _connection to null.
		/// </summary>
		public static void CloseConnection ()
		{
			if (_connection != null)
			{
				_connection.Close ();
				_connection = null;
			}
		}

		/// <summary>
		/// Tell IBus to exit
		/// </summary>
		public static bool ExitIBus()
		{
#if DISABLE_KEYBOARDSWITCHING
			return true;
#else
			if (EngineAvailable)
			{
				var ibus = new InputBusWrapper (_connection);
				ibus.InputBus.Exit (false);
				Thread.Sleep(100);
				CloseConnection();
			}
			return !EngineAvailable;
#endif
		}

		/// <summary>
		/// Tell IBus to restart
		/// </summary>
		public static bool RestartIBus()
		{
#if DISABLE_KEYBOARDSWITCHING
			return true;
#else
			if (EngineAvailable)
			{
				var ibus = new InputBusWrapper (_connection);
				ibus.InputBus.Exit (true);
				Thread.Sleep(100);
				CloseConnection();
			}
			return EngineAvailable;
#endif
		}

		/// <summary>
		/// Start IBus manually
		/// </summary>
		public static bool StartIBus()
		{
#if DISABLE_KEYBOARDSWITCHING
			return true;
#else
			if (!EngineAvailable)
			{
				ProcessStartInfo startInfo = new ProcessStartInfo("ibus-daemon",
															  "-x -d -r");
				Process.Start(startInfo);
				Thread.Sleep(100);
			}
			return EngineAvailable;
#endif
		}

#if !DISABLE_KEYBOARDSWITCHING
		private static void NotifyUserOfProblem(IBusError error, string message)
		{
			switch (error)
			{
				default:
					Console.WriteLine("About to notify '{0}'", message);
					ErrorReport.NotifyUserOfProblem(new ShowOncePerSessionBasedOnExactMessagePolicy(), message);
					break;
			}
		}
#endif

		public static string DefaultKeyboardName
		{
			get { return _defaultKeyboard != null ? _defaultKeyboard.Id : String.Empty; }
		}

		private static InputContextWrapper TryGetFocusedInputContext(InputBusWrapper ibus)
		{
			for (int i = 1; ; ++i)
			{
				try
				{
					string inputContextPath = ibus.GetFocusedInputContextPath();
					//Console.WriteLine("TryGetFocusedInputContext: {0} ICPath {1}", i, inputContextPath);
					return new InputContextWrapper(_connection, inputContextPath);
				}
				catch (Exception)
				{
					if (i > 10)
					{
						throw;
					}
					Thread.Sleep(20);
				}
			}
		}

		public static void ActivateKeyboard (string name)
		{
#if !DISABLE_KEYBOARDSWITCHING
			EnsureConnection ();

			if(String.Compare(name,"None") != 0 && !HasKeyboardNamed(name))
			{
				throw new ArgumentOutOfRangeException("name", name, "IBus does not have a Keyboard of that name.");
			}

			if (_timer == null)
			{
				_timer = new Timer { Interval = 10 };
				_timer.Tick += OnTimerTick;
			}
			_requestActivateName = name;
			_timer.Start();
#endif
		}

		private static void OnTimerTick(object sender, EventArgs e)
		{
			try
			{
				_timer.Stop();
				var ibus = new InputBusWrapper(_connection);
				var inputContextBus = TryGetFocusedInputContext(ibus);
				inputContextBus.InputContext.SetEngine(_requestActivateName);
			}
			catch (Exception x)
			{
				Console.WriteLine ("OnTimerTick Exception: {0}", x.Message);
			}
		}

		/// <summary>
		/// Helper function the builds a list of Active Keyboards
		/// </summary>
		protected static IEnumerable<KeyboardController.KeyboardDescriptor> GetKeyboardDescriptors ()
		{
			if (_connection == null)
			{
				yield break;
			}
			var ibus = new InputBusWrapper (_connection);
			object[] engines = ibus.InputBus.ListActiveEngines ();
			if (EngineVersion == IBusEngineVersion.NotConfigured && (engines).Length>0)
			{
				GetEngineVersion(engines[0]);
			}

			for (int i = 0; i < (engines).Length; ++i)
			{
				if (EngineVersion == IBusEngineVersion.V1)
				{
					var v = GetKeyboardDescriptor_V1(engines[i]);
					yield return v;
				}
				else if (EngineVersion == IBusEngineVersion.V2)
				{
					var v = GetKeyboardDescriptor_V2(engines[i]);
					yield return v;
				}
				else if (EngineVersion == IBusEngineVersion.V3)
				{
					var v = GetKeyboardDescriptor_V3(engines[i]);
					yield return v;
				}
				else if (EngineVersion == IBusEngineVersion.V4)
				{
					var v = GetKeyboardDescriptor_V4(engines[i]);
					yield return v;
				}
				else
					throw new ApplicationException("Unknown IBus engine version");
			}
		}

		protected static string GetLanguageFromId (string lang_id)
		{
			String language;
			try
			{
				CultureInfo ci = CultureInfo.GetCultureInfo(lang_id);
				language = ci.DisplayName; // + "(" + lang_id + ")";
				if (language.Contains("Invariant"))
				{
					language = "Other"; // + "(" + lang_id + ")";
				}
			}
			catch
			{
				if (StandardTags.IsValidIso639LanguageCode(lang_id))
				{
					var codes = StandardTags.ValidIso639LanguageCodes;
					var lang = codes.Where(code => code.Code == lang_id).First();
					language = lang.Name; // + "(" + lang_id + ")";
				}
				else
				{
					language = "Unknown"; // + "(" + lang_id + ")";
				}
			}
			return language;
		}

		/// <summary>
		/// Helper function the builds a list of Active Keyboards
		/// </summary>
		protected static KeyboardController.KeyboardDescriptor GetKeyboardDescriptor_V1 (object engine)
		{
			var engineDesc = (IBusEngineDesc_v1)Convert.ChangeType (engine, typeof(IBusEngineDesc_v1));
			var v = new KeyboardController.KeyboardDescriptor
					{
						Id = engineDesc.longname,
						ShortName = engineDesc.name,
						LongName = GetLanguageFromId(engineDesc.language) + " - " + engineDesc.name,
						engine = KeyboardController.Engines.IBus
					};
			//Console.WriteLine("got description: {0}", engineDesc.ToString ());

			return v;
		}

		/// <summary>
		/// Helper function the builds a list of Active Keyboards
		/// </summary>
		protected static KeyboardController.KeyboardDescriptor GetKeyboardDescriptor_V2 (object engine)
		{
			var engineDesc = (IBusEngineDesc_v2)Convert.ChangeType (engine, typeof(IBusEngineDesc_v2));
			var v = new KeyboardController.KeyboardDescriptor
					{
						Id = engineDesc.longname,
						ShortName = engineDesc.name,
						LongName = GetLanguageFromId(engineDesc.language) + " - " + engineDesc.name,
						engine = KeyboardController.Engines.IBus
					};
			//Console.WriteLine("got description: {0}", engineDesc.ToString ());

			return v;
		}

		/// <summary>
		/// Helper function the builds a list of Active Keyboards
		/// </summary>
		protected static KeyboardController.KeyboardDescriptor GetKeyboardDescriptor_V3 (object engine)
		{
			var engineDesc = (IBusEngineDesc_v3)Convert.ChangeType (engine, typeof(IBusEngineDesc_v3));
			var v = new KeyboardController.KeyboardDescriptor
					{
						Id = engineDesc.longname,
						ShortName = engineDesc.name,
						LongName = GetLanguageFromId(engineDesc.language) + " - " + engineDesc.name,
						engine = KeyboardController.Engines.IBus
					};
			//Console.WriteLine("got description: {0}", engineDesc.ToString ());

			return v;
		}

		/// <summary>
		/// Helper function the builds a list of Active Keyboards
		/// </summary>
		protected static KeyboardController.KeyboardDescriptor GetKeyboardDescriptor_V4 (object engine)
		{
			var engineDesc = (IBusEngineDesc_v4)Convert.ChangeType (engine, typeof(IBusEngineDesc_v4));
			var v = new KeyboardController.KeyboardDescriptor
					{
						Id = engineDesc.longname,
						ShortName = engineDesc.name,
						LongName = GetLanguageFromId(engineDesc.language) + " - " + engineDesc.name,
						engine = KeyboardController.Engines.IBus
					};
			//Console.WriteLine("got description: {0}", engineDesc.ToString ());

			return v;
		}


		public static List<KeyboardController.KeyboardDescriptor> KeyboardDescriptors
		{
			get
			{
				try
				{
					EnsureConnection();
				}
				catch {};
				return new List<KeyboardController.KeyboardDescriptor>(GetKeyboardDescriptors () );
			}
		}

		public static bool EngineAvailable
		{
			get
			{
				try
				{
					EnsureConnection ();
					return (_connection != null);
				} catch
				{
					return false;
				}
			}
		}

		public static void Deactivate ()
		{
			// Do nothing. IBus and mono maintain one input context per control.
			// So, there is no need to deactivate it as the keyboard is not global.

			// But from ibus 1.3 it can be global
			//trying to find out how to deactivate
			if (!String.IsNullOrEmpty(DefaultKeyboardName))
			{
				try
				{
					ActivateKeyboard(DefaultKeyboardName);
				}
				catch (ErrorReport.ProblemNotificationSentToUserException e)
				{
					if (!e.Message.Contains("seem to be running"))
						throw;
				};
			}

/*			if (_connection != null)
			{
				ActivateKeyboard("None");
			}
			CloseConnection();
			*/
		}

		public static void GetEngineVersion (object engine)
		{
			try
			{
				var engineDesc = (IBusEngineDesc_v4)Convert.ChangeType (engine, typeof(IBusEngineDesc_v4));
				_engineVersion = IBusEngineVersion.V4;
			}
			catch (System.InvalidCastException)
			{
				try
				{
					var engineDesc = (IBusEngineDesc_v3)Convert.ChangeType (engine, typeof(IBusEngineDesc_v3));
					_engineVersion = IBusEngineVersion.V3;
				}
				catch (System.InvalidCastException)
				{
					try
					{
						var engineDesc = (IBusEngineDesc_v2)Convert.ChangeType (engine, typeof(IBusEngineDesc_v2));
						_engineVersion = IBusEngineVersion.V2;
					}
					catch (System.InvalidCastException)
					{
						_engineVersion = IBusEngineVersion.V1;
					}
				}
			}
		}


		public static IBusEngineVersion EngineVersion
		{
			get
			{
				return _engineVersion;
			}
		}

		public static bool HasKeyboardNamed (string name)
		{
#if DEBUG
			Console.WriteLine("Looking for {0} in:",name);
			foreach (var keyboard in GetKeyboardDescriptors())
			{
				Console.WriteLine(keyboard.Id);
			}
#endif
			bool retval = GetKeyboardDescriptors().Any(d => d.Id.Equals(name));
#if DEBUG
			Console.WriteLine(retval ? "succeeded" : "failed");
#endif
			return retval;
		}

		public static string GetActiveKeyboard ()
		{
			EnsureConnection ();
#if DISABLE_KEYBOARDSWITCHING
			return "Manual";
#else
			var ibus = new InputBusWrapper (_connection);
			try
			{
				var inputContextBus = TryGetFocusedInputContext(ibus);
				object engine = inputContextBus.InputContext.GetEngine ();
				if (engine == null)
				{
					throw new ApplicationException ("Focused Input Context doesn't have an active Keyboard/Engine");
				}
				if (EngineVersion == IBusEngineVersion.NotConfigured)
				{
					GetEngineVersion(engine);
				}
				string enginename;
				if (EngineVersion == IBusEngineVersion.V3)
				{
					var engineDesc = (IBusEngineDesc_v3)Convert.ChangeType (engine, typeof(IBusEngineDesc_v3));
					enginename = engineDesc.longname;
				}
				else if (EngineVersion == IBusEngineVersion.V4)
				{
					var engineDesc = (IBusEngineDesc_v4)Convert.ChangeType (engine, typeof(IBusEngineDesc_v4));
					enginename = engineDesc.longname;
				}
				else if (EngineVersion == IBusEngineVersion.V2)
				{
					var engineDesc = (IBusEngineDesc_v2)Convert.ChangeType (engine, typeof(IBusEngineDesc_v2));
					enginename = engineDesc.longname;
				}
				else if (EngineVersion == IBusEngineVersion.V1)
				{
					var engineDesc = (IBusEngineDesc_v1)Convert.ChangeType (engine, typeof(IBusEngineDesc_v1));
					enginename = engineDesc.longname;
				}
				else
					throw new ApplicationException("Unknown IBus engine version");
				Console.WriteLine ("GetActiveKeyboard got keyboard {0}", enginename);
				return enginename;
			}
			catch (Exception)
			{
				ErrorReport.NotifyUserOfProblem(
					new ShowOncePerSessionBasedOnExactMessagePolicy(),
					"Could not get ActiveKeyboard"
				);
				return String.Empty;
			}
#endif
		}
	}
}
#endif