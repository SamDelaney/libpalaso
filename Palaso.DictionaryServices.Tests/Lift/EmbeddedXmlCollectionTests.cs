﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using Palaso.Lift;
using Palaso.Tests.Code;

namespace Palaso.DictionaryServices.Tests.Lift
{
	[TestFixture]
	public class EmbeddedXmlCollectionIClonableGenericTests : IClonableGenericTests<IPalasoDataObjectProperty>
	{
		public override IPalasoDataObjectProperty CreateNewClonable()
		{
			return new EmbeddedXmlCollection();
		}

		public override string ExceptionList
		{
			//_parent: We are doing top down clones. Children shouldn't make clones of their parents, but parents of their children.
			get { return "|_parent|"; }
		}

		public override Dictionary<Type, object> DefaultValuesForTypes
		{
			get
			{
				return new Dictionary<Type, object>
						   {
							   {typeof (List<string>), new List<string> {"one", "two"}}
						   };
			}
		}
	}

	[TestFixture]
	public class EmbeddedXmlCollectionTests
	{
	}
}