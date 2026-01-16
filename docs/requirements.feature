Feature: Main application layout

  Scenario: Viewing the main view
    Given the application is started
    When I am in the main view
    Then I see a navigation pane at the top with tabs:
      | Dashboard |
      | History   |
      | Search    |
      | Settings  |
    And I see an editable rich text task list for new tasks in the middle
    And next to the title of the new tasks view I see a button to move all new tasks to "Uncategorized"
    And next to the title of the new tasks view I see a button to expand and restore the new tasks view to full screen
    And I see an editable rich text main task list below


Feature: Task completion

  Scenario: Completing a task
    Given a task exists in any task list
    And the task is not completed
    When I mark the task as completed
    Then the task title is shown with strikethrough styling
    And the task text color is gray
    And the completion timestamp of the task is saved

  Scenario: Undoing task completion
    Given a task exists in the history
    And the task is completed
    When I mark the task as not completed
    Then the strikethrough styling is removed
    And the task text color is black
    And the task is moved back to the dashboard


Feature: Dashboard task categorization

  Scenario: Viewing new tasks
    Given I am on the dashboard tab
    When I view the new tasks task list
    Then I see only tasks under the category "New"

  Scenario: Viewing categorized tasks
    Given I am on the dashboard tab
    When I view the main task list
    Then I see tasks grouped under named categories
    And tasks under the category "New" are not shown


Feature: Task categories

  Scenario: Available task categories
    Given tasks exist in the system
    Then the following task categories are available:
      | New           |
      | Uncategorized |
      | Week starting on YYYY-MM-DD |
      | No date       |
      | Repeatable    |
      | Notes         |

  Scenario: Weekly categories visibility
    Given tasks are categorized by week
    Then only the next 4 weeks are shown
    And any past week that still has tasks is shown


Feature: Floating task ribbon

  Scenario: Showing the task ribbon on focus
    Given I am on the dashboard tab
    And a task has focus
    When the task is focused
    Then a floating ribbon appears below the task
    And the ribbon provides an option to categorize the task
    And the ribbon provides an option to set or unset task recurrence

  Scenario: Configuring task recurrence
    Given a task has focus
    When I configure the task recurrence
    Then I can set the recurrence to:
      | repeatable |
      | daily      |
      | weekly     |
      | monthly    |
    And for weekly recurrence I can select weekdays
    And for monthly recurrence I can select days of the month


Feature: Repeatable tasks

  Scenario: Creating repeatable tasks
    Given a task is marked as repeatable
    Then the task is categorized under "Repeatable"
    And the recurrence configuration is saved

  Scenario: Generating recurring tasks
    Given repeatable tasks exist
    When the day changes or the application is started
    Then new tasks are created based on recurrence rules
    And tasks are generated for the next 4 weeks


Feature: Daily task cleanup

  Scenario: Hiding completed tasks from the dashboard
    Given a task was completed on a day earlier than today
    When the day changes or the application is started
    Then the task is hidden from the main task list on the dashboard


Feature: History view

  Scenario: Viewing task completion history
    Given I navigate to the history tab
    Then I see a bar chart showing how many tasks were completed each day
    And the chart shows only the last 180 days
    And I see a list of completed tasks grouped by completion date

  Scenario: Restoring a completed task from history
    Given I am on the history tab
    And a completed task exists
    When I mark the task as not completed
    Then the task is removed from the history
    And the task appears in the dashboard


Feature: Search

  Scenario: Viewing the search tab
    Given I navigate to the search tab
    Then I see a search bar
    And I see a search button
    And I see an empty results area

  Scenario: Searching before any search is executed
    Given I am on the search tab
    And no search has been executed
    Then the results area is empty

  Scenario: Searching with no results
    Given I am on the search tab
    When I search for a term that does not exist
    Then I see a message indicating no results

  Scenario: Searching with results
    Given I am on the search tab
    When I search for a term that exists in any task
    Then I see matching tasks from any view regardless of status
    And the tasks are shown as read-only
    And the matching text is highlighted in the search results


Feature: Multi-instance consistency

  Scenario: Reflecting task edits across instances
    Given the application is open in two instances
    And the same task is visible in both instances
    When the task is edited in one instance
    Then the updated content is shown in the other instance

  Scenario: Reflecting task completion across instances
    Given the application is open in two instances
    And a task exists
    When the task is completed in one instance
    Then the task completion state is updated in the other instance
