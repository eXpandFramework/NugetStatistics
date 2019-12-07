using System;
using System.Collections;
using System.Linq;
using System.Reactive.Linq;

namespace XpandNugetStats.Models{
    public static class ModelExtensions{
        public static IObservable<Shields> ToShields<T>(this IObservable<T > source,string label,string color="Green"){
            return source.SelectMany(_ => _.GetType().IsArray
                    ? ((IEnumerable) _).Cast<object>().ToObservable()
                    : new[]{_}.Cast<object>().ToObservable())
                .Select(o => {
                    var strings = $"{o}".Split("=");
                    var message = strings[0];
                    if (strings.Length == 2){
                        label = message;
                        message = strings[1];
                    }
                    var shields = new Shields{Message = message,Label = label,Color = color};
                    return shields;
                });
        }

    }
}