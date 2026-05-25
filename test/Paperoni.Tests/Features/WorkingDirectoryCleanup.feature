Feature: Working Directory Cleanup
  As a system operator
  I want old album working directories to be cleaned up automatically
  So that disk space does not grow unboundedly

  Scenario: Cleanup deletes album directories older than retention period
    Given the working directory contains album "100" last modified 10 days ago
    And the working directory contains album "200" last modified 2 days ago
    And the working directory retention is 7 days
    When cleanup runs
    Then album directory "100" is deleted
    And album directory "200" still exists

  Scenario: Cleanup skips non-numeric directories
    Given the working directory contains a non-numeric directory "logs"
    And the working directory retention is 7 days
    When cleanup runs
    Then directory "logs" still exists

  Scenario: Cleanup does nothing when retention is disabled
    Given the working directory contains album "300" last modified 30 days ago
    And the working directory retention is 0 days
    When cleanup runs
    Then album directory "300" still exists

  Scenario: Cleanup deletes multiple old directories and logs summary
    Given the working directory contains album "400" last modified 10 days ago
    And the working directory contains album "500" last modified 15 days ago
    And the working directory contains album "600" last modified 1 days ago
    And the working directory retention is 7 days
    When cleanup runs
    Then album directory "400" is deleted
    And album directory "500" is deleted
    And album directory "600" still exists