Feature: AI Integration
As a user
I want to ask questions to an AI model
So that I can get accurate answers

Scenario: AI correctly identifies the capital of France
    When I ask "What is the capital of France?"
    Then the answer should contain "Paris"

@ignore
Scenario: Long text
    When I ask "Please write a 1000 words long shot story about dragons"
    Then the answer should contain "dragon"

Scenario: AI can read and describe two images
    When I send two images: a red square and a blue circle
    And I ask "Describe both images and their colors" with the images
    Then the answer should contain "red"
    And the answer should contain "blue"

@ignore
# tool calling only sometimes works with small gemma models
Scenario: AI uses tool calling to get current weather
    When I ask about the weather for a hike
    Then the answer should contain weather information
