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

  Scenario: Completing an empty task removes it
    Given a task exists in any task list
    And the task has no visible text in the title or subcontent
    When I mark the task as completed
    Then the task is removed
    And the task is not shown in the history


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
      | Notes         |
      | Repeatable    |

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
    And weekly recurring tasks are generated only for the current week


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

  Scenario: Moving completed tasks to history
    Given I am on the history tab
    And completed tasks exist for today
    When I click the Move completed to history button
    Then the completed tasks are removed from the dashboard
    And the tasks appear in the history list

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


Feature: Subcontent list editing

  Scenario: Subcontent lines and soft line breaks
    Given a task has subcontent
    When I press Shift+Enter inside a subcontent line
    Then a line break is inserted within the same subcontent item

  Scenario: Creating a new subcontent item with Enter
    Given a task has subcontent
    When I press Enter inside a subcontent line
    Then a new subcontent list item is created below
    And if the cursor was in the middle of the line the trailing text moves to the new item

  Scenario: Merging subcontent items with Backspace
    Given a task has multiple subcontent list items
    When I press Backspace at the start of a subcontent item
    Then the current item merges with the item above

  Scenario: Indenting and outdenting subcontent items
    Given a task has subcontent list items
    When I press Tab inside a subcontent item
    Then the item is indented one level
    When I press Shift+Tab inside a subcontent item
    Then the item is outdented one level

Feature: Task restructuring via Tab

  Scenario: Tab in title converts to subcontent of previous task
    Given multiple tasks exist in a list
    And a task title has focus
    When I press Tab in the task title
    Then the task becomes subcontent of the previous task
    And any existing subcontent is moved under the previous task

  Scenario: Shift+Tab at outermost level splits subcontent into a new task
    Given a task has subcontent items at the outermost level
    And the cursor is within a subcontent item
    When I press Shift+Tab
    Then a new task is created below the current task
    And the sibling-level subcontent is moved to the new task


Feature: Arrow key navigation

  Scenario: Down arrow from title enters subcontent
    Given a task title has focus
    When I press the down arrow key
    Then the focus moves to the beginning of the first subcontent line

  Scenario: Up arrow from title jumps to previous task
    Given a task title has focus
    And there is a task above
    When I press the up arrow key
    Then the focus moves to the end of the last subcontent line of the task above

  Scenario: Down arrow from subcontent to next title or new task
    Given a task has subcontent
    And the cursor is at the end of the subcontent
    When I press the down arrow key
    Then the focus moves to the beginning of the next task title if one exists
    And if no task exists below a new task is created with an empty title

  Scenario: Up arrow from subcontent to title
    Given a task has subcontent
    And the cursor is at the beginning of the subcontent
    When I press the up arrow key
    Then the focus moves to the task title


Feature: Version visibility and update metadata

  Scenario: Viewing the app version
    Given the application is started
    When I view the settings tab
    Then I see the application version in the About section
    And I see the application version in a footer area

  Scenario: App version persisted on startup
    Given the application is started
    When the server initializes
    Then the current binary version is logged
    And the stored app_version value is logged
    And the schema version is logged
    And the stored app_version is updated after startup


Feature: Update safety invariants

  Scenario: Updating the app without data loss
    Given the application is closed
    When I replace the app binaries
    Then the data folder remains untouched
    And the application starts with the existing data


Feature: Backups and maintenance

  Scenario: Backing up data from settings
    Given I am on the settings tab
    When I click the Backup button
    Then a backup is created
    And the last backup time is shown

  Scenario: Reindexing search from settings
    Given I am on the settings tab
    When I click the Reindex button
    Then the search index is rebuilt
    And the last reindex time is shown

  Scenario: Warning banner visibility
    Given the application is started
    When maintenance warnings exist
    Then I see a warning banner
    And I can dismiss a warning


Feature: Attachments

  Scenario: Pasting an image into rich text
    Given a task title or subcontent has focus
    When I paste an image
    Then the image is uploaded
    And the image is inserted into the editor

  Scenario: Resizing an attachment
    Given an image is inserted into a task
    When I select the image
    Then a resize handle appears
    And the resized width is preserved

  Scenario: Removing an attachment
    Given an image is selected in the editor
    When I press Backspace
    Then the image is removed


Feature: Drag and drop ordering

  Scenario: Reordering tasks within a category
    Given a category has multiple tasks
    When I drag a task within the category
    Then the task order is updated

  Scenario: Moving tasks across categories
    Given tasks exist in multiple categories
    When I drag a task to a different category
    Then the task is moved to that category


Feature: Recurrence controls

  Scenario: Allowed recurrence types
    Given a task has recurrence controls visible
    Then I can set the recurrence to:
      | weekly  |
      | monthly |

  Scenario: Weekly recurrence selection
    Given a task is set to weekly recurrence
    When I select weekdays
    Then the selected weekdays are saved

  Scenario: Monthly recurrence selection
    Given a task is set to monthly recurrence
    When I enter month days
    Then the selected days are saved

  Scenario: Remembering recurrence configuration
    Given a task has a weekly or monthly recurrence configured
    When I move it out of Repeatable
    Then the recurrence configuration is retained for later use


Feature: Category interaction

  Scenario: Category actions appear on hover
    Given a task is visible on the dashboard
    When I hover over the task
    Then category action buttons are shown

  Scenario: This week scheduling on category change
    Given a task is moved into the This week category
    Then its scheduled date is set to today

  Scenario: Dragging within This week changes the scheduled day
    Given a task is in the This week category
    When I drag the task to a different weekday group
    Then its scheduled date matches the target weekday


Feature: Dashboard layout and scrolling

  Scenario: Horizontal columns on wide screens
    Given I am on the dashboard tab
    Then tasks are shown in horizontal columns by category
    And each column scrolls vertically
    And the dashboard scrolls horizontally across columns

  Scenario: This week weekday grouping
    Given tasks exist in the This week category
    Then tasks are grouped by weekday
    And weekday headers are sticky within the column


Feature: Title-only tasks and deletion

  Scenario: Creating title-only tasks
    Given a task has no subcontent
    When I press Enter at the end of the title
    Then a new task is created below
    And focus moves to the new task title

  Scenario: Removing the last subcontent item
    Given a task has a single empty subcontent item
    When I press Backspace in the subcontent
    Then the subcontent item is removed
    And focus moves to the task title

  Scenario: Deleting an empty task
    Given a task has no title and no subcontent
    When I press Backspace
    Then the task is deleted
    And focus moves to the previous task if it exists


Feature: Keyboard shortcuts

  Scenario: Formatting shortcuts
    Given a rich text editor has focus
    When I press Ctrl+B or Cmd+B
    Then the selected text is bolded
    When I press Ctrl+I or Cmd+I
    Then the selected text is italicized

  Scenario: Highlight shortcuts
    Given a rich text editor has focus
    When I press Ctrl+3 or Cmd+3
    Then green highlight is toggled
    When I press Ctrl+4 or Cmd+4
    Then yellow highlight is toggled
    When I press Ctrl+5 or Cmd+5
    Then red highlight is toggled

  Scenario: Subcontent checkbox shortcut
    Given a subcontent list item has focus
    When I press Ctrl+1 or Cmd+1
    Then an empty checkbox marker is inserted at the start of the line
    When I press Ctrl+1 or Cmd+1 again
    Then the checkbox marker is toggled to checked
    When I press Ctrl+1 or Cmd+1 a third time
    Then the checkbox marker is removed

  Scenario: Subcontent star shortcut
    Given a subcontent list item has focus
    When I press Ctrl+2 or Cmd+2
    Then a yellow star marker (⭐) is inserted at the start of the line
    When I press Ctrl+2 or Cmd+2 again
    Then the star marker is removed
    And if a checkbox marker is present it appears before the star marker

  Scenario: Search shortcut
    Given I am not focused on an editor
    When I press Ctrl+F or Cmd+F
    Then the Search tab is activated


Feature: Search matching

  Scenario: Partial word matches
    Given tasks exist with words in titles or subcontent
    When I search for a partial word
    Then matches include occurrences within the word
