namespace ManagedCode.LlmTestServer.Providers;

public static class LlmTestServerProviderOperationIds
{
    public static class SystemOne
    {
        public const string DecisionsCreate = "systemone.decisions.create";
    }
    public static class Cloudflare
    {
        public const string ClefDecisionsCreate = "clef.decisions.create";
        public const string ClefFlashDecisionsCreate = "clef-flash.decisions.create";
    }
    public static class OpenAI
    {
        public const string DecisionsCreate = "decisions.create";
        public const string ModelsList = "models.list";
        public const string ChatCompletionsCreate = "chat.completions.create";
        public const string ResponsesCreate = "responses.create";
        public const string EmbeddingsCreate = "embeddings.create";
        public const string ImagesCreate = "images.create";
        public const string ImagesEditsCreate = "images.edits.create";
        public const string ImagesVariationsCreate = "images.variations.create";
        public const string AudioSpeechCreate = "audio.speech.create";
        public const string AudioTranscriptionsCreate = "audio.transcriptions.create";
        public const string AudioTranslationsCreate = "audio.translations.create";
        public const string VideosCreate = "videos.create";
        public const string VideosList = "videos.list";
        public const string VideosRetrieve = "videos.retrieve";
        public const string VideosDelete = "videos.delete";
        public const string VideosContentRetrieve = "videos.content.retrieve";
        public const string VideosEditsCreate = "videos.edits.create";
        public const string VideosExtensionsCreate = "videos.extensions.create";
        public const string VideosRemix = "videos.remix";
        public const string VideosCharactersCreate = "videos.characters.create";
        public const string VideosCharactersRetrieve = "videos.characters.retrieve";
    }

    public static class AzureOpenAI
    {
        public const string V1ChatCompletionsCreate = "v1.chat.completions.create";
        public const string V1ResponsesCreate = "v1.responses.create";
        public const string V1EmbeddingsCreate = "v1.embeddings.create";
        public const string V1ImagesCreate = "v1.images.create";
        public const string V1AudioTranscriptionsCreate = "v1.audio.transcriptions.create";

        public const string ChatCompletionsCreate = OpenAI.ChatCompletionsCreate;
        public const string EmbeddingsCreate = OpenAI.EmbeddingsCreate;
        public const string ImagesCreate = OpenAI.ImagesCreate;
        public const string AudioSpeechCreate = OpenAI.AudioSpeechCreate;
        public const string AudioTranscriptionsCreate = OpenAI.AudioTranscriptionsCreate;
        public const string AudioTranslationsCreate = OpenAI.AudioTranslationsCreate;
        public const string VideoGenerationJobsCreate = "video.generation.jobs.create";
        public const string VideoGenerationJobsList = "video.generation.jobs.list";
        public const string VideoGenerationJobsRetrieve = "video.generation.jobs.retrieve";
        public const string VideoGenerationJobsDelete = "video.generation.jobs.delete";
        public const string VideoGenerationsRetrieve = "video.generations.retrieve";
        public const string VideoGenerationsThumbnailRetrieve = "video.generations.thumbnail.retrieve";
        public const string VideoGenerationsContentRetrieve = "video.generations.content.retrieve";
        public const string VideoGenerationsContentHead = "video.generations.content.head";
    }

    public static class MicrosoftFoundry
    {
        public const string V1ChatCompletionsCreate = AzureOpenAI.V1ChatCompletionsCreate;
        public const string V1ResponsesCreate = AzureOpenAI.V1ResponsesCreate;
        public const string V1EmbeddingsCreate = AzureOpenAI.V1EmbeddingsCreate;
        public const string V1AudioTranscriptionsCreate = AzureOpenAI.V1AudioTranscriptionsCreate;

        public const string ChatCompletionsCreate = OpenAI.ChatCompletionsCreate;
        public const string EmbeddingsCreate = OpenAI.EmbeddingsCreate;
        public const string ModelsChatCompletionsCreate = "models.chat.completions.create";
        public const string ModelsEmbeddingsCreate = "models.embeddings.create";
    }

    public static class Anthropic
    {
        public const string MessagesCreate = "messages.create";
    }

    public static class Gemini
    {
        public const string ModelsGenerateContent = "models.generateContent";
        public const string ModelsStreamGenerateContent = "models.streamGenerateContent";
        public const string ModelsEmbedContent = "models.embedContent";
        public const string ModelsPredictLongRunningVideo = "models.predictLongRunning.video";
        public const string ModelsOperationsGetVideo = "models.operations.get.video";
        public const string FilesGetGeneratedVideo = "files.get.generatedVideo";
    }

    public static class Groq
    {
        public const string ChatCompletionsCreate = OpenAI.ChatCompletionsCreate;
        public const string ResponsesCreate = OpenAI.ResponsesCreate;
        public const string AudioSpeechCreate = OpenAI.AudioSpeechCreate;
        public const string AudioTranscriptionsCreate = OpenAI.AudioTranscriptionsCreate;
        public const string AudioTranslationsCreate = OpenAI.AudioTranslationsCreate;
        public const string ModelsList = OpenAI.ModelsList;
    }

    public static class Mistral
    {
        public const string ChatComplete = "chat.complete";
        public const string EmbeddingsCreate = OpenAI.EmbeddingsCreate;
    }

    public static class Ollama
    {
        public const string ChatCreate = "chat.create";
        public const string EmbeddingsCreate = OpenAI.EmbeddingsCreate;
    }

    public static class Cohere
    {
        public const string ChatCreate = Ollama.ChatCreate;
        public const string EmbedCreate = "embed.create";
    }

    public static class Bedrock
    {
        public const string Converse = "converse";
        public const string ConverseStream = "converseStream";
        public const string InvokeModel = "invokeModel";
        public const string InvokeModelWithResponseStream = "invokeModelWithResponseStream";
    }

    public static class OpenRouter
    {
        public const string ChatCompletionsCreate = OpenAI.ChatCompletionsCreate;
        public const string ResponsesCreate = OpenAI.ResponsesCreate;
        public const string ModelsList = OpenAI.ModelsList;
    }

    public static class DeepSeek
    {
        public const string ChatCompletionsCreate = OpenAI.ChatCompletionsCreate;
        public const string ModelsList = OpenAI.ModelsList;
    }

    public static class Perplexity
    {
        public const string SonarCreate = "sonar.create";
    }
}
