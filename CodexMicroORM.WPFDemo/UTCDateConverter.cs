/***********************************************************************
Copyright 2018 CodeX Enterprises LLC

Licensed under the Apache License, Version 2.0 (the "License");
you may not use this file except in compliance with the License.
You may obtain a copy of the License at

    http://www.apache.org/licenses/LICENSE-2.0

Unless required by applicable law or agreed to in writing, software
distributed under the License is distributed on an "AS IS" BASIS,
WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
See the License for the specific language governing permissions and
limitations under the License.

Major Changes:
12/2017    0.2     Initial release (Joel Champagne)
***********************************************************************/
using System;
using System.Windows.Data;
using System.Windows.Markup;

namespace CodexMicroORM.WPFDemo
{
    [ValueConversion(typeof(DateTime?), typeof(string))]
    public class UTCDateConverter : MarkupExtension, IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter,
                          System.Globalization.CultureInfo culture)
        {
            if (value == null)
            {
                return null;
            }

            return new DateTime(((DateTime?)value).Value.Ticks, DateTimeKind.Utc).ToLocalTime().ToString("G");
        }

        public object ConvertBack(object value, Type targetType, object parameter,
                        System.Globalization.CultureInfo culture)
        {
            return null;
        }

        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            return this;
        }
    }
}
