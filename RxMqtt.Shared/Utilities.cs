namespace RxMqtt.Shared
{
    public static class Utilities
    {
        public static bool IsTopicMatch(string topic, string topicFilter)
        {
            var topicParts = topic.Split('/');
            var topicFilterParts = topicFilter.Split('/');

            var loopCount = topicParts.Length;

            if (topicFilterParts.Length != topicParts.Length)
            {
                return false;
            }

            for (var i = 0; i < loopCount; i++)
            {

                if (!topicParts[i].Equals(topicFilterParts[i]) &&
                    !topicFilterParts[i].Equals("#") &&
                    !topicFilterParts[i].Equals("+"))
                {
                    return false;
                }
            }

            return true;
        }
    }
}